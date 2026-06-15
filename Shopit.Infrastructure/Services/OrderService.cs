using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class OrderService : IOrderService
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;

    public OrderService(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    public async Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request)
    {
        // 1. Fetch cart
        var cart = await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Inventory)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CartStatus.Active);

        if (cart == null || !cart.CartItems.Any())
            throw new ValidationException("Cart is empty.");

        // 2. Check stock for ALL items upfront
        var outOfStockItems = cart.CartItems
            .Where(ci => ci.Product.Inventory == null || ci.Product.Inventory.Quantity < ci.Quantity)
            .Select(ci => ci.Product.Name)
            .ToList();

        if (outOfStockItems.Any())
            throw new ValidationException($"Insufficient stock for: {string.Join(", ", outOfStockItems)}.");

        // 3. Compute totals
        var subtotal = cart.CartItems.Sum(ci => ci.Product.Price * ci.Quantity);
        decimal discountAmount = 0;

        if (cart.Coupon != null)
        {
            if (cart.Coupon.DiscountType == CouponDiscountType.Percent)
                discountAmount = subtotal * (cart.Coupon.DiscountValue / 100);
            else
                discountAmount = cart.Coupon.DiscountValue;
        }

        var totalAmount = subtotal - discountAmount;
        if (totalAmount < 0) totalAmount = 0;

        // 4. Begin transaction
        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // 5. Create Order
            var order = new Order
            {
                UserId = userId,
                Status = OrderStatus.Pending,
                TotalAmount = totalAmount,
                DiscountAmount = discountAmount,
                ShippingAddress = request.ShippingAddress,
                CouponId = cart.CouponId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // 6. Create OrderItems and deduct inventory
            foreach (var cartItem in cart.CartItems)
            {
                var orderItem = new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = cartItem.ProductId,
                    ProductNameSnapshot = cartItem.Product.Name,
                    Quantity = cartItem.Quantity,
                    UnitPrice = cartItem.Product.Price,
                    Subtotal = cartItem.Product.Price * cartItem.Quantity
                };

                _context.OrderItems.Add(orderItem);

                // Deduct inventory
                cartItem.Product.Inventory!.Quantity -= cartItem.Quantity;
            }

            // 7. Clear cart
            _context.CartItems.RemoveRange(cart.CartItems);
            cart.CouponId = null;

            // 8. SaveChanges and commit
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            // 9. Fire and forget confirmation email
            var userEmail = (await _context.Users.FindAsync(userId))!.Email;
            _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, userEmail));

            // Return response
            var orderWithItems = await _context.Orders
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.Product)
                .FirstAsync(o => o.Id == order.Id);

            return MapToResponse(orderWithItems);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static OrderResponse MapToResponse(Order order) => new()
    {
        Id = order.Id,
        Status = order.Status.ToString(),
        TotalAmount = order.TotalAmount,
        DiscountAmount = order.DiscountAmount,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        Items = order.OrderItems.Select(oi => new OrderItemResponse
        {
            Id = oi.Id,
            ProductId = oi.ProductId,
            ProductName = oi.ProductNameSnapshot,
            Quantity = oi.Quantity,
            UnitPrice = oi.UnitPrice,
            Subtotal = oi.Subtotal
        }).ToList()
    };
}