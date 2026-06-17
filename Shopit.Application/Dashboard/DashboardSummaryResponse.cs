namespace Shopit.Application.DTOs.Dashboard;

public class DashboardSummaryResponse
{
    public decimal TotalRevenue { get; set; }
    public int TotalOrders { get; set; }
    public int TotalCustomers { get; set; }
    public int LowStockCount { get; set; }
    public int TodaysNewOrders { get; set; }
}