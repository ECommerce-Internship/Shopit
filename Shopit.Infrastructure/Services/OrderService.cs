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
            .Include(c => c.CartItems)
                .ThenInclude(ci => ci.Product)
                    .ThenInclude(p => p.Store)
            .Include(c => c.Coupon)
            .FirstOrDefaultAsync(c => c.UserId == userId && c.Status == CartStatus.Active);

        if (cart == null || !cart.CartItems.Any())
            throw new ValidationException("Cart is empty.");

        // A store must be Approved before it can sell (SCRUM-132).
        var unavailableItems = cart.CartItems
            .Where(ci => ci.Product.Store.Status != StoreStatus.Approved)
            .Select(ci => ci.Product.Name)
            .ToList();

        if (unavailableItems.Any())
            throw new ValidationException($"These products are not currently available for purchase: {string.Join(", ", unavailableItems)}.");

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

            // SCRUM-135 fan-out: one StoreOrder per distinct store in the cart. Commission is
            // computed on each store's gross subtotal; the coupon/discount stays at the parent.
            foreach (var storeGroup in cart.CartItems.GroupBy(ci => ci.Product.StoreId))
            {
                var store = storeGroup.First().Product.Store;
                var storeSubtotal = storeGroup.Sum(ci => ci.Product.Price * ci.Quantity);
                var commission = Math.Round(storeSubtotal * store.CommissionRate, 2, MidpointRounding.AwayFromZero);

                var storeOrder = new StoreOrder
                {
                    OrderId = order.Id,
                    StoreId = storeGroup.Key,
                    Status = OrderStatus.Pending,
                    SubTotal = storeSubtotal,
                    CommissionAmount = commission,
                    SellerNetAmount = storeSubtotal - commission
                };

                foreach (var cartItem in storeGroup)
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
            }

            // A single simulated payment covers the whole multi-seller order (Order <-> Payment 1:1).
            _context.Payments.Add(new Payment
            {
                OrderId = order.Id,
                Amount = totalAmount,
                Method = PaymentMethod.Card,
                Status = PaymentStatus.Paid,
                TransactionRef = Guid.NewGuid().ToString("N"),
                PaidAt = DateTime.UtcNow
            });

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
                    .ThenInclude(so => so.Store)
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
                .ThenInclude(so => so.Store)
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
                .ThenInclude(so => so.Store)
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
                .ThenInclude(so => so.Store)
            .Include(o => o.StoreOrders)
                .ThenInclude(so => so.StoreOrderItems)
                    .ThenInclude(soi => soi.Product)
                        .ThenInclude(p => p.Inventory)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

        if (order == null)
            throw new NotFoundException($"Order with ID {orderId} was not found.");

        var storeOrders = order.StoreOrders.ToList();

        // The buyer may cancel the whole order only while every part is still Pending.
        if (storeOrders.Any(so => so.Status != OrderStatus.Pending))
            throw new ValidationException($"Order cannot be cancelled because it is already {RollUpStatus(storeOrders)}.");

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
                .ThenInclude(so => so.Store)
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

    public async Task<IReadOnlyList<SellerStoreOrderResponse>> GetMyStoreOrdersAsync(int userId)
    {
        var storeOrders = await _context.StoreOrders
            .AsNoTracking()
            .Include(so => so.Store)
            .Include(so => so.Order)
            .Include(so => so.StoreOrderItems)
            .Where(so => so.Store.OwnerUserId == userId)
            .OrderByDescending(so => so.Order.CreatedAt)
            .ToListAsync();

        return storeOrders.Select(MapToSellerResponse).ToList();
    }

    public async Task<SellerStoreOrderResponse> GetStoreOrderByIdAsync(int storeOrderId, int userId, bool isAdmin)
    {
        var storeOrder = await _context.StoreOrders
            .AsNoTracking()
            .Include(so => so.Store)
            .Include(so => so.Order)
            .Include(so => so.StoreOrderItems)
            .FirstOrDefaultAsync(so => so.Id == storeOrderId);

        if (storeOrder is null)
            throw new NotFoundException($"Store order with ID {storeOrderId} was not found.");

        if (!isAdmin && storeOrder.Store.OwnerUserId != userId)
            throw new ForbiddenException("You can only view store orders for your own stores.");

        return MapToSellerResponse(storeOrder);
    }

    public async Task<SellerStoreOrderResponse> UpdateStoreOrderStatusAsync(int storeOrderId, UpdateOrderStatusRequest request, int userId, bool isAdmin)
    {
        var storeOrder = await _context.StoreOrders
            .Include(so => so.Store)
            .Include(so => so.Order)
            .Include(so => so.StoreOrderItems)
                .ThenInclude(soi => soi.Product)
                    .ThenInclude(p => p.Inventory)
            .FirstOrDefaultAsync(so => so.Id == storeOrderId);

        if (storeOrder is null)
            throw new NotFoundException($"Store order with ID {storeOrderId} was not found.");

        if (!isAdmin && storeOrder.Store.OwnerUserId != userId)
            throw new ForbiddenException("You can only manage store orders for your own stores.");

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

        if (!validProgressions[storeOrder.Status].Contains(newStatus))
            throw new ValidationException($"Cannot transition store order from {storeOrder.Status} to {newStatus}.");

        // Cancelling a single StoreOrder restocks only that store's items.
        if (newStatus == OrderStatus.Cancelled)
        {
            foreach (var item in storeOrder.StoreOrderItems)
            {
                if (item.Product?.Inventory != null)
                    item.Product.Inventory.Quantity += item.Quantity;
            }
        }

        storeOrder.Status = newStatus;
        await _context.SaveChangesAsync();

        return MapToSellerResponse(storeOrder);
    }

    private static OrderSummaryResponse MapToSummary(Order order) => new()
    {
        Id = order.Id,
        Status = RollUpStatus(order.StoreOrders).ToString(),
        TotalAmount = order.TotalAmount,
        DiscountAmount = order.DiscountAmount,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        ItemCount = order.StoreOrders.Sum(so => so.StoreOrderItems.Count),
        PaymentStatus = order.Payment?.Status.ToString(),
        StoreOrders = order.StoreOrders
            .Select(so => new StoreOrderSummaryResponse
            {
                StoreId = so.StoreId,
                StoreName = so.Store?.Name ?? string.Empty,
                Status = so.Status.ToString(),
                SubTotal = so.SubTotal,
                ItemCount = so.StoreOrderItems.Count
            }).ToList()
    };

    private static OrderResponse MapToResponse(Order order) => new()
    {
        Id = order.Id,
        Status = RollUpStatus(order.StoreOrders).ToString(),
        TotalAmount = order.TotalAmount,
        DiscountAmount = order.DiscountAmount,
        ShippingAddress = order.ShippingAddress,
        CreatedAt = order.CreatedAt,
        Items = order.StoreOrders
            .SelectMany(so => so.StoreOrderItems)
            .Select(MapItem).ToList(),
        StoreOrders = order.StoreOrders
            .Select(so => new StoreOrderResponse
            {
                StoreId = so.StoreId,
                StoreName = so.Store?.Name ?? string.Empty,
                Status = so.Status.ToString(),
                SubTotal = so.SubTotal,
                Items = so.StoreOrderItems.Select(MapItem).ToList()
            }).ToList()
    };

    private static OrderItemResponse MapItem(StoreOrderItem soi) => new()
    {
        Id = soi.Id,
        ProductId = soi.ProductId,
        ProductName = soi.ProductNameSnapshot,
        Quantity = soi.Quantity,
        UnitPrice = soi.UnitPrice,
        Subtotal = soi.Subtotal
    };

    // Parent order summary: all StoreOrders cancelled => Cancelled; otherwise the least-advanced
    // active StoreOrder (Pending < Processing < Shipped < Delivered).
    private static OrderStatus RollUpStatus(IEnumerable<StoreOrder> storeOrders)
    {
        var active = storeOrders.Where(so => so.Status != OrderStatus.Cancelled).ToList();
        if (active.Count == 0)
            return storeOrders.Any() ? OrderStatus.Cancelled : OrderStatus.Pending;
        return active.Min(so => so.Status);
    }

    private static SellerStoreOrderResponse MapToSellerResponse(StoreOrder so) => new()
    {
        StoreOrderId = so.Id,
        OrderId = so.OrderId,
        StoreId = so.StoreId,
        StoreName = so.Store?.Name ?? string.Empty,
        Status = so.Status.ToString(),
        SubTotal = so.SubTotal,
        CommissionAmount = so.CommissionAmount,
        SellerNetAmount = so.SellerNetAmount,
        ShippingAddress = so.Order?.ShippingAddress ?? string.Empty,
        CreatedAt = so.Order?.CreatedAt ?? default,
        Items = so.StoreOrderItems.Select(MapItem).ToList()
    };
}
