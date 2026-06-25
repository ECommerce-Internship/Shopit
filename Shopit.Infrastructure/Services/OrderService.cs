using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public OrderService(AppDbContext context, IEmailService emailService, IServiceScopeFactory serviceScopeFactory)
    {
        _context = context;
        _emailService = emailService;
        _serviceScopeFactory = serviceScopeFactory;
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
            if (cart.Coupon.UsageLimit.HasValue && cart.Coupon.UsageCount >= cart.Coupon.UsageLimit.Value)
                throw new ValidationException($"Coupon '{cart.Coupon.Code}' has reached its usage limit.");

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
                TotalAmount = totalAmount,
                DiscountAmount = discountAmount,
                ShippingAddress = request.ShippingAddress,
                CouponId = cart.CouponId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // SCRUM-129 bridge: until per-seller fan-out exists, all items go into a single
            // StoreOrder owned by the products' store. Commission logic is deferred (rate 0).
            var storeOrder = new StoreOrder
            {
                OrderId = order.Id,
                StoreId = cart.CartItems.First().Product.StoreId,
                Status = OrderStatus.Pending,
                SubTotal = subtotal,
                CommissionAmount = 0,
                SellerNetAmount = subtotal
            };

            foreach (var cartItem in cart.CartItems)
            {
                storeOrder.StoreOrderItems.Add(new StoreOrderItem
                {
                    ProductId = cartItem.ProductId,
                    ProductNameSnapshot = cartItem.Product.Name,
                    Quantity = cartItem.Quantity,
                    UnitPrice = cartItem.Product.Price,
                    Subtotal = cartItem.Product.Price * cartItem.Quantity
                });

                cartItem.Product.Inventory!.Quantity -= cartItem.Quantity;
            }

            _context.StoreOrders.Add(storeOrder);

            if (cart.Coupon != null)
                cart.Coupon.UsageCount += 1;

            _context.CartItems.RemoveRange(cart.CartItems);
            cart.CouponId = null;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync();
                throw new ValidationException("One or more items in your order are no longer available in the requested quantity. Please review your cart and try again.");
            }

            await transaction.CommitAsync();

            var userEmail = (await _context.Users.FindAsync(userId))!.Email;
            var orderId = order.Id;

            _ = Task.Run(async () =>
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
                await emailService.SendOrderConfirmationAsync(orderId, userEmail);
            });

            var orderWithItems = await _context.Orders
                .Include(o => o.StoreOrders)
                    .ThenInclude(so => so.StoreOrderItems)
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
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Orders
            .Include(o => o.StoreOrders)
                .ThenInclude(so => so.StoreOrderItems)
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
            .Include(o => o.StoreOrders)
                .ThenInclude(so => so.StoreOrderItems)
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
            .Include(o => o.StoreOrders)
                .ThenInclude(so => so.StoreOrderItems)
                    .ThenInclude(soi => soi.Product)
                        .ThenInclude(p => p.Inventory)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        var storeOrders = order.StoreOrders.ToList();
        var currentStatus = storeOrders.FirstOrDefault()?.Status ?? OrderStatus.Pending;

        if (currentStatus != OrderStatus.Pending)
            throw new ValidationException($"Order cannot be cancelled because it is already {currentStatus}.");

        foreach (var storeOrder in storeOrders)
            storeOrder.Status = OrderStatus.Cancelled;

        foreach (var item in storeOrders.SelectMany(so => so.StoreOrderItems))
        {
            if (item.Product?.Inventory != null)
                item.Product.Inventory.Quantity += item.Quantity;
        }

        await _context.SaveChangesAsync();

        return MapToResponse(order);
    }

    public async Task<PaginatedResponse<OrderSummaryResponse>> GetAllOrdersAsync(int page, int pageSize, string? status, DateTime? from, DateTime? to)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;

        var query = _context.Orders
            .Include(o => o.StoreOrders)
                .ThenInclude(so => so.StoreOrderItems)
            .Include(o => o.Payment)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, true, out var parsedStatus))
            query = query.Where(o => o.StoreOrders.Any(so => so.Status == parsedStatus));

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
            .Include(o => o.StoreOrders)
                .ThenInclude(so => so.StoreOrderItems)
                    .ThenInclude(soi => soi.Product)
                        .ThenInclude(p => p.Inventory)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        if (!Enum.TryParse<OrderStatus>(request.Status, true, out var newStatus))
            throw new ValidationException($"Invalid order status: {request.Status}.");

        var storeOrders = order.StoreOrders.ToList();
        var currentStatus = storeOrders.FirstOrDefault()?.Status ?? OrderStatus.Pending;

        var validProgressions = new Dictionary<OrderStatus, List<OrderStatus>>
        {
            { OrderStatus.Pending, new List<OrderStatus> { OrderStatus.Processing, OrderStatus.Cancelled } },
            { OrderStatus.Processing, new List<OrderStatus> { OrderStatus.Shipped } },
            { OrderStatus.Shipped, new List<OrderStatus> { OrderStatus.Delivered } },
            { OrderStatus.Delivered, new List<OrderStatus>() },
            { OrderStatus.Cancelled, new List<OrderStatus>() }
        };

        if (!validProgressions[currentStatus].Contains(newStatus))
            throw new ValidationException($"Cannot transition order from {currentStatus} to {newStatus}.");

        if (newStatus == OrderStatus.Cancelled)
        {
            foreach (var item in storeOrders.SelectMany(so => so.StoreOrderItems))
            {
                if (item.Product?.Inventory != null)
                    item.Product.Inventory.Quantity += item.Quantity;
            }
        }

        foreach (var storeOrder in storeOrders)
            storeOrder.Status = newStatus;

        await _context.SaveChangesAsync();

        return MapToResponse(order);
    }

    private static OrderSummaryResponse MapToSummary(Order order) => new()
    {
        Id = order.Id,
        Status = (order.StoreOrders.FirstOrDefault()?.Status ?? OrderStatus.Pending).ToString(),
        TotalAmount = order.TotalAmount,
        DiscountAmount = order.DiscountAmount,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        ItemCount = order.StoreOrders.Sum(so => so.StoreOrderItems.Count),
        PaymentStatus = order.Payment?.Status.ToString()
    };

    private static OrderResponse MapToResponse(Order order) => new()
    {
        Id = order.Id,
        Status = (order.StoreOrders.FirstOrDefault()?.Status ?? OrderStatus.Pending).ToString(),
        TotalAmount = order.TotalAmount,
        DiscountAmount = order.DiscountAmount,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        Items = order.StoreOrders
            .SelectMany(so => so.StoreOrderItems)
            .Select(soi => new OrderItemResponse
            {
                Id = soi.Id,
                ProductId = soi.ProductId,
                ProductName = soi.ProductNameSnapshot,
                Quantity = soi.Quantity,
                UnitPrice = soi.UnitPrice,
                Subtotal = soi.Subtotal
            }).ToList()
    };
}
