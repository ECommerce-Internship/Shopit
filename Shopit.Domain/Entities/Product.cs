namespace Shopit.Domain.Entities;
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public Inventory? Inventory { get; set; }
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<StoreOrderItem> StoreOrderItems { get; set; } = new List<StoreOrderItem>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();

    public float[]? Embedding { get; set; }
}