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
        var cart = await _context.Carts
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Inventory)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CartStatus.Active);

        if (cart == null || !cart.CartItems.Any())
            throw new ValidationException("Cart is empty.");

        var outOfStockItems = cart.CartItems
            .Where(ci => ci.Product.Inventory == null || ci.Product.Inventory.Quantity < ci.Quantity)
            .Select(ci => ci.Product.Name)
            .ToList();

        if (outOfStockItems.Any())
            throw new ValidationException($"Insufficient stock for: {string.Join(", ", outOfStockItems)}.");

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

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
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
                cartItem.Product.Inventory!.Quantity -= cartItem.Quantity;
            }

            _context.CartItems.RemoveRange(cart.CartItems);
            cart.CouponId = null;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            var userEmail = (await _context.Users.FindAsync(userId))!.Email;
            _ = Task.Run(() => _emailService.SendOrderConfirmationAsync(order.Id, userEmail));

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

    public async Task<PaginatedResponse<OrderSummaryResponse>> GetMyOrdersAsync(int userId, int page, int pageSize)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt);

        var totalCount = await query.CountAsync();

        var orders = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResponse<OrderSummaryResponse>
        {
            Items = orders.Select(MapToSummary).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<OrderResponse> GetOrderByIdAsync(int orderId, int userId, bool isAdmin)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .Include(o => o.Payment)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (!isAdmin && order.UserId != userId)
            throw new ForbiddenException("You do not have access to this order.");

        return MapToResponse(order);
    }

    public async Task<OrderResponse> CancelOrderAsync(int orderId, int userId)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (order.Status != OrderStatus.Pending)
            throw new ValidationException($"Order cannot be cancelled because it is already {order.Status}.");

        order.Status = OrderStatus.Cancelled;
        await _context.SaveChangesAsync();

        return MapToResponse(order);
    }

    public async Task<PaginatedResponse<OrderSummaryResponse>> GetAllOrdersAsync(int page, int pageSize, string? status, DateTime? from, DateTime? to)
    {
        var query = _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
            query = query.Where(o => o.Status == parsedStatus);

        if (from.HasValue)
            query = query.Where(o => o.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(o => o.CreatedAt <= to.Value);

        query = query.OrderByDescending(o => o.CreatedAt);

        var totalCount = await query.CountAsync();

        var orders = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PaginatedResponse<OrderSummaryResponse>
        {
            Items = orders.Select(MapToSummary).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<OrderResponse> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequest request)
    {
        var order = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Product)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (!Enum.TryParse<OrderStatus>(request.Status, true, out var newStatus))
            throw new ValidationException($"Invalid order status: {request.Status}.");

        var validProgressions = new Dictionary<OrderStatus, List<OrderStatus>>
        {
            { OrderStatus.Pending, new List<OrderStatus> { OrderStatus.Processing, OrderStatus.Cancelled } },
            { OrderStatus.Processing, new List<OrderStatus> { OrderStatus.Shipped } },
            { OrderStatus.Shipped, new List<OrderStatus> { OrderStatus.Delivered } },
            { OrderStatus.Delivered, new List<OrderStatus>() },
            { OrderStatus.Cancelled, new List<OrderStatus>() }
        };

        if (!validProgressions[order.Status].Contains(newStatus))
            throw new ValidationException($"Cannot transition order from {order.Status} to {newStatus}.");

        order.Status = newStatus;
        await _context.SaveChangesAsync();

        return MapToResponse(order);
    }

    private static OrderSummaryResponse MapToSummary(Order order) => new()
    {
        Id = order.Id,
        Status = order.Status.ToString(),
        TotalAmount = order.TotalAmount,
        DiscountAmount = order.DiscountAmount,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        ItemCount = order.OrderItems.Count,
        PaymentStatus = order.Payment?.Status.ToString()
    };

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