using Shopit.Application.DTOs.Dashboard;

namespace Shopit.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync();
    Task<IEnumerable<RevenueByPeriodResponse>> GetRevenueByPeriodAsync(string period);
    Task<IEnumerable<TopProductResponse>> GetTopProductsAsync();
    Task<IEnumerable<NewCustomersByPeriodResponse>> GetNewCustomersAsync(string period);
    Task<IEnumerable<OrdersByStatusResponse>> GetOrdersByStatusAsync();
}