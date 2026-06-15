namespace Shopit.Application.DTOs;

public class OrderSummaryResponse
{
    public int Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string ShippingAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ItemCount { get; set; }
    public string? PaymentStatus { get; set; }
}