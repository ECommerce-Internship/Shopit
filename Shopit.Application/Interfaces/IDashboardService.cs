using Shopit.Application.DTOs.Dashboard;

namespace Shopit.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync();
    Task<IEnumerable<RevenueByPeriodResponse>> GetRevenueByPeriodAsync(string period);
    Task<IEnumerable<TopProductResponse>> GetTopProductsAsync();
    Task<IEnumerable<NewCustomersByPeriodResponse>> GetNewCustomersAsync(string period);
    Task<IEnumerable<OrdersByStatusResponse>> GetOrdersByStatusAsync();

    // Seller-scoped views (metrics restricted to the caller's own stores).
    Task<SellerDashboardSummaryResponse> GetSellerSummaryAsync(int userId);
    Task<IEnumerable<TopProductResponse>> GetSellerTopProductsAsync(int userId);
    Task<IEnumerable<RevenueByPeriodResponse>> GetSellerRevenueByPeriodAsync(int userId, string period);
    Task<IEnumerable<OrdersByStatusResponse>> GetSellerOrdersByStatusAsync(int userId);
}
