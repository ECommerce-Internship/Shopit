namespace Shopit.Application.Models;

public class OrderConfirmationMessage
{
    public int OrderId { get; set; }
    public string ToEmail { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
    public List<StoreOrderMessage> StoreOrders { get; set; } = new();
}

public class StoreOrderMessage
{
    public string StoreName { get; set; } = string.Empty;
    public decimal SubTotal { get; set; }
    public List<OrderItemMessage> Items { get; set; } = new();
}

public class OrderItemMessage
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
