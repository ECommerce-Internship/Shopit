namespace Shopit.Domain.Entities;

public class Inventory
{
    public int Id { get; set; }
    public int Quantity { get; set; } = 0;
    public int LowStockThreshold { get; set; } = 10;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = null!;
}