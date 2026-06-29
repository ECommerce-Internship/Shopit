namespace Shopit.Application.DTOs;

public class StoreOrderResponse
{
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
}
