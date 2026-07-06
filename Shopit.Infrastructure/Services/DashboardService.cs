using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Dashboard;
using Shopit.Application.Interfaces;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;
    private readonly ICacheService _cache;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromSeconds(120);

    public DashboardService(AppDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync()
    {
        const string cacheKey = "dashboard:summary";

        var cached = await _cache.GetAsync<DashboardSummaryResponse>(cacheKey);
        if (cached is not null)
            return cached;

        var today = DateTime.UtcNow.Date;

        var totalRevenue = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

        var totalCommission = await _context.StoreOrders
            .Where(so => so.Status != OrderStatus.Cancelled)
            .SumAsync(so => (decimal?)so.CommissionAmount) ?? 0;

        var totalOrders = await _context.Orders.CountAsync();

        var totalCustomers = await _context.Users
            .CountAsync(u => u.Role == UserRole.Customer);

        var lowStockCount = await _context.Inventories
            .CountAsync(i => i.Quantity <= i.LowStockThreshold);

        var todaysNewOrders = await _context.Orders
            .CountAsync(o => o.CreatedAt.Date == today);

        var result = new DashboardSummaryResponse
        {
            TotalRevenue = totalRevenue,
            TotalCommission = totalCommission,
            TotalOrders = totalOrders,
            TotalCustomers = totalCustomers,
            LowStockCount = lowStockCount,
            TodaysNewOrders = todaysNewOrders
        };

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<RevenueByPeriodResponse>> GetRevenueByPeriodAsync(string period)
    {
        var cacheKey = $"dashboard:revenue:{period}";

        var cached = await _cache.GetAsync<List<RevenueByPeriodResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var rows = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .Join(_context.Orders,
                  p => p.OrderId,
                  o => o.Id,
                  (p, o) => new { p.Amount, o.CreatedAt })
            .ToListAsync();

        var result = GroupRevenue(rows.Select(x => (x.Amount, x.CreatedAt)), period);

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<TopProductResponse>> GetTopProductsAsync()
    {
        const string cacheKey = "dashboard:top-products";

        var cached = await _cache.GetAsync<List<TopProductResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var result = await _context.StoreOrderItems
            .Include(soi => soi.Product)
            .GroupBy(soi => new { soi.ProductId, soi.Product.Name })
            .Select(g => new TopProductResponse
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                UnitsSold = g.Sum(soi => soi.Quantity),
                Revenue = g.Sum(soi => soi.Quantity * soi.UnitPrice)
            })
            .OrderByDescending(x => x.UnitsSold)
            .Take(10)
            .ToListAsync();

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<NewCustomersByPeriodResponse>> GetNewCustomersAsync(string period)
    {
        var cacheKey = $"dashboard:new-customers:{period}";

        var cached = await _cache.GetAsync<List<NewCustomersByPeriodResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var customers = await _context.Users
            .Where(u => u.Role == UserRole.Customer)
            .Select(u => u.CreatedAt)
            .ToListAsync();

        List<NewCustomersByPeriodResponse> result;

        if (period == "week")
        {
            result = customers
                .GroupBy(d => new {
                    d.Year,
                    Week = System.Globalization.ISOWeek.GetWeekOfYear(d)
                })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Week)
                .Select(g => new NewCustomersByPeriodResponse
                {
                    Period = $"{g.Key.Year}-W{g.Key.Week:D2}",
                    NewCustomers = g.Count()
                })
                .ToList();
        }
        else if (period == "month")
        {
            result = customers
                .GroupBy(d => new { d.Year, d.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new NewCustomersByPeriodResponse
                {
                    Period = $"{g.Key.Year}-{g.Key.Month:D2}",
                    NewCustomers = g.Count()
                })
                .ToList();
        }
        else
        {
            result = customers
                .GroupBy(d => d.Date)
                .OrderBy(g => g.Key)
                .Select(g => new NewCustomersByPeriodResponse
                {
                    Period = g.Key.ToString("yyyy-MM-dd"),
                    NewCustomers = g.Count()
                })
                .ToList();
        }

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<OrdersByStatusResponse>> GetOrdersByStatusAsync()
    {
        const string cacheKey = "dashboard:orders-by-status";

        var cached = await _cache.GetAsync<List<OrdersByStatusResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var result = await _context.StoreOrders
            .GroupBy(so => so.Status)
            .Select(g => new OrdersByStatusResponse
            {
                Status = g.Key.ToString(),
                Count = g.Count()
            })
            .ToListAsync();

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    // ---- Seller-scoped views (metrics restricted to the caller's own stores) ----

    public async Task<SellerDashboardSummaryResponse> GetSellerSummaryAsync(int userId)
    {
        var cacheKey = $"dashboard:seller:{userId}:summary";

        var cached = await _cache.GetAsync<SellerDashboardSummaryResponse>(cacheKey);
        if (cached is not null)
            return cached;

        var today = DateTime.UtcNow.Date;

        var ownStoreOrders = _context.StoreOrders
            .Where(so => so.Store.OwnerUserId == userId);

        var active = ownStoreOrders.Where(so => so.Status != OrderStatus.Cancelled);

        var grossSales = await active.SumAsync(so => (decimal?)so.SubTotal) ?? 0;
        var totalCommission = await active.SumAsync(so => (decimal?)so.CommissionAmount) ?? 0;
        var netEarnings = await active.SumAsync(so => (decimal?)so.SellerNetAmount) ?? 0;
        var totalOrders = await ownStoreOrders.CountAsync();
        var todaysNewOrders = await ownStoreOrders.CountAsync(so => so.Order.CreatedAt.Date == today);

        var lowStockCount = await _context.Inventories
            .CountAsync(i => i.Product.Store.OwnerUserId == userId
                          && i.Quantity <= i.LowStockThreshold);

        var result = new SellerDashboardSummaryResponse
        {
            GrossSales = grossSales,
            TotalCommission = totalCommission,
            NetEarnings = netEarnings,
            TotalOrders = totalOrders,
            LowStockCount = lowStockCount,
            TodaysNewOrders = todaysNewOrders
        };

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<TopProductResponse>> GetSellerTopProductsAsync(int userId)
    {
        var cacheKey = $"dashboard:seller:{userId}:top-products";

        var cached = await _cache.GetAsync<List<TopProductResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var result = await _context.StoreOrderItems
            .Where(soi => soi.StoreOrder.Store.OwnerUserId == userId
                       && soi.StoreOrder.Status != OrderStatus.Cancelled)
            .GroupBy(soi => new { soi.ProductId, soi.Product.Name })
            .Select(g => new TopProductResponse
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                UnitsSold = g.Sum(soi => soi.Quantity),
                Revenue = g.Sum(soi => soi.Quantity * soi.UnitPrice)
            })
            .OrderByDescending(x => x.UnitsSold)
            .Take(10)
            .ToListAsync();

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<RevenueByPeriodResponse>> GetSellerRevenueByPeriodAsync(int userId, string period)
    {
        var cacheKey = $"dashboard:seller:{userId}:revenue:{period}";

        var cached = await _cache.GetAsync<List<RevenueByPeriodResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var rows = await _context.StoreOrders
            .Where(so => so.Store.OwnerUserId == userId
                      && so.Status != OrderStatus.Cancelled)
            .Select(so => new { Amount = so.SubTotal, so.Order.CreatedAt })
            .ToListAsync();

        var result = GroupRevenue(rows.Select(x => (x.Amount, x.CreatedAt)), period);

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    public async Task<IEnumerable<OrdersByStatusResponse>> GetSellerOrdersByStatusAsync(int userId)
    {
        var cacheKey = $"dashboard:seller:{userId}:orders-by-status";

        var cached = await _cache.GetAsync<List<OrdersByStatusResponse>>(cacheKey);
        if (cached is not null)
            return cached;

        var result = await _context.StoreOrders
            .Where(so => so.Store.OwnerUserId == userId)
            .GroupBy(so => so.Status)
            .Select(g => new OrdersByStatusResponse
            {
                Status = g.Key.ToString(),
                Count = g.Count()
            })
            .ToListAsync();

        await _cache.SetAsync(cacheKey, result, CacheExpiry);

        return result;
    }

    // Groups revenue rows into day/week/month buckets (in-memory; ISO week for "week").
    private static List<RevenueByPeriodResponse> GroupRevenue(
        IEnumerable<(decimal Amount, DateTime CreatedAt)> rows, string period)
    {
        if (period == "week")
        {
            return rows
                .GroupBy(x => new {
                    x.CreatedAt.Year,
                    Week = System.Globalization.ISOWeek.GetWeekOfYear(x.CreatedAt)
                })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Week)
                .Select(g => new RevenueByPeriodResponse
                {
                    Period = $"{g.Key.Year}-W{g.Key.Week:D2}",
                    Revenue = g.Sum(x => x.Amount),
                    OrderCount = g.Count()
                })
                .ToList();
        }

        if (period == "month")
        {
            return rows
                .GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new RevenueByPeriodResponse
                {
                    Period = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Revenue = g.Sum(x => x.Amount),
                    OrderCount = g.Count()
                })
                .ToList();
        }

        return rows
            .GroupBy(x => x.CreatedAt.Date)
            .OrderBy(g => g.Key)
            .Select(g => new RevenueByPeriodResponse
            {
                Period = g.Key.ToString("yyyy-MM-dd"),
                Revenue = g.Sum(x => x.Amount),
                OrderCount = g.Count()
            })
            .ToList();
    }
}
