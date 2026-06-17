namespace Shopit.Application.DTOs.Dashboard;

public class RevenueByPeriodResponse
{
    public string Period { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
}