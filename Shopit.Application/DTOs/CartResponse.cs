namespace Shopit.Application.DTOs;

public class CartResponse
{
    public int Id { get; set; }
    public List<CartItemResponse> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public string? CouponCode { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public decimal? DiscountAmount { get; set; }
    public decimal FinalTotal { get; set; }
}