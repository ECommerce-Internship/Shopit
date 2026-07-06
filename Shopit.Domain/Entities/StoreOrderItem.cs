namespace Shopit.Domain.Entities;

public class StoreOrderItem
{
    public int Id { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }

    public int StoreOrderId { get; set; }
    public StoreOrder StoreOrder { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}
