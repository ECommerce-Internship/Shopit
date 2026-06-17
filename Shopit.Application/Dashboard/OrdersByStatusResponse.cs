namespace Shopit.Application.DTOs.Dashboard;

public class OrdersByStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}