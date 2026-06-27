namespace Shopit.Application.DTOs;

public class SellerStoreOrderResponse
{
    public int StoreOrderId { get; set; }
    public int OrderId { get; set; }
    public int StoreId { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal SellerNetAmount { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<OrderItemResponse> Items { get; set; } = new();
}
