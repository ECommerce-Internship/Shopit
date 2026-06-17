namespace Shopit.Application.DTOs.Dashboard;

public class NewCustomersByPeriodResponse
{
    public string Period { get; set; } = string.Empty;
    public int NewCustomers { get; set; }
}