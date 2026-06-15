using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Dashboard;
using Shopit.Application.Interfaces;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Data;
using StackExchange.Redis;
using System.Text.Json;

namespace Shopit.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;
    private readonly IDatabase _cache;
    private const int CacheExpirySeconds = 120;

    public DashboardService(AppDbContext context, IConnectionMultiplexer redis)
    {
        _context = context;
        _cache = redis.GetDatabase();
    }

    public async Task<DashboardSummaryResponse> GetSummaryAsync()
    {
        const string cacheKey = "dashboard:summary";

        var cached = await _cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<DashboardSummaryResponse>(cached.ToString())!;

        var today = DateTime.UtcNow.Date;

        var totalRevenue = await _context.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .SumAsync(p => (decimal?)p.Amount) ?? 0;

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
            TotalOrders = totalOrders,
            TotalCustomers = totalCustomers,
            LowStockCount = lowStockCount,
            TodaysNewOrders = todaysNewOrders
        };

        await _cache.StringSetAsync(cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(CacheExpirySeconds));

        return result;
    }

    public async Task<IEnumerable<RevenueByPeriodResponse>> GetRevenueByPeriodAsync(string period)
    {
        var cacheKey = $"dashboard:revenue:{period}";

        var cached = await _cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<IEnumerable<RevenueByPeriodResponse>>(cached.ToString())!;

        var paidPayments = _context.Payments
            .Where(p => p.Status == PaymentStatus.Paid)
            .Join(_context.Orders,
                  p => p.OrderId,
                  o => o.Id,
                  (p, o) => new { p.Amount, o.CreatedAt });

        List<RevenueByPeriodResponse> result;

        if (period == "week")
        {
            var raw = await paidPayments.ToListAsync();
            result = raw
                .GroupBy(x => System.Globalization.ISOWeek.GetWeekOfYear(x.CreatedAt))
                .OrderBy(g => g.Key)
                .Select(g => new RevenueByPeriodResponse
                {
                    Period = $"Week {g.Key}",
                    Revenue = g.Sum(x => x.Amount),
                    OrderCount = g.Count()
                })
                .ToList();
        }
        else if (period == "month")
        {
            result = await paidPayments
                .GroupBy(x => new { x.CreatedAt.Year, x.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new RevenueByPeriodResponse
                {
                    Period = $"{g.Key.Year}-{g.Key.Month:D2}",
                    Revenue = g.Sum(x => x.Amount),
                    OrderCount = g.Count()
                })
                .ToListAsync();
        }
        else
        {
            result = await paidPayments
                .GroupBy(x => x.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new RevenueByPeriodResponse
                {
                    Period = g.Key.ToString("yyyy-MM-dd"),
                    Revenue = g.Sum(x => x.Amount),
                    OrderCount = g.Count()
                })
                .ToListAsync();
        }

        await _cache.StringSetAsync(cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(CacheExpirySeconds));

        return result;
    }

    public async Task<IEnumerable<TopProductResponse>> GetTopProductsAsync()
    {
        const string cacheKey = "dashboard:top-products";

        var cached = await _cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<IEnumerable<TopProductResponse>>(cached.ToString())!;

        var result = await _context.OrderItems
            .Include(oi => oi.Product)
            .GroupBy(oi => new { oi.ProductId, oi.Product.Name })
            .Select(g => new TopProductResponse
            {
                ProductId = g.Key.ProductId,
                ProductName = g.Key.Name,
                UnitsSold = g.Sum(oi => oi.Quantity),
                Revenue = g.Sum(oi => oi.Quantity * oi.UnitPrice)
            })
            .OrderByDescending(x => x.UnitsSold)
            .Take(10)
            .ToListAsync();

        await _cache.StringSetAsync(cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(CacheExpirySeconds));

        return result;
    }

    public async Task<IEnumerable<NewCustomersByPeriodResponse>> GetNewCustomersAsync(string period)
    {
        var cacheKey = $"dashboard:new-customers:{period}";

        var cached = await _cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<IEnumerable<NewCustomersByPeriodResponse>>(cached.ToString())!;

        var customers = _context.Users
            .Where(u => u.Role == UserRole.Customer);

        List<NewCustomersByPeriodResponse> result;

        if (period == "week")
        {
            var raw = await customers.Select(u => u.CreatedAt).ToListAsync();
            result = raw
                .GroupBy(d => System.Globalization.ISOWeek.GetWeekOfYear(d))
                .OrderBy(g => g.Key)
                .Select(g => new NewCustomersByPeriodResponse
                {
                    Period = $"Week {g.Key}",
                    NewCustomers = g.Count()
                })
                .ToList();
        }
        else if (period == "month")
        {
            result = await customers
                .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new NewCustomersByPeriodResponse
                {
                    Period = $"{g.Key.Year}-{g.Key.Month:D2}",
                    NewCustomers = g.Count()
                })
                .ToListAsync();
        }
        else
        {
            result = await customers
                .GroupBy(u => u.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new NewCustomersByPeriodResponse
                {
                    Period = g.Key.ToString("yyyy-MM-dd"),
                    NewCustomers = g.Count()
                })
                .ToListAsync();
        }

        await _cache.StringSetAsync(cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(CacheExpirySeconds));

        return result;
    }

    public async Task<IEnumerable<OrdersByStatusResponse>> GetOrdersByStatusAsync()
    {
        const string cacheKey = "dashboard:orders-by-status";

        var cached = await _cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
            return JsonSerializer.Deserialize<IEnumerable<OrdersByStatusResponse>>(cached.ToString())!;

        var result = await _context.Orders
            .GroupBy(o => o.Status)
            .Select(g => new OrdersByStatusResponse
            {
                Status = g.Key.ToString(),
                Count = g.Count()
            })
            .ToListAsync();

        await _cache.StringSetAsync(cacheKey,
            JsonSerializer.Serialize(result),
            TimeSpan.FromSeconds(CacheExpirySeconds));

        return result;
    }
}