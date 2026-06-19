namespace Shopit.Application.Models;

public class OrderConfirmationMessage
{
    public int OrderId { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public List<OrderItemMessage> Items { get; set; } = new();
    public decimal Total { get; set; }
}

public class OrderItemMessage
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}