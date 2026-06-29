namespace Shopit.Application.DTOs;

public class StoreOrderSummaryResponse
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public int ItemCount { get; set; }
}
