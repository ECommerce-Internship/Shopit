using Shopit.Domain.Enums;

namespace Shopit.Domain.Entities;

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public StoreStatus Status { get; set; } = StoreStatus.Pending;
    public decimal CommissionRate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int OwnerUserId { get; set; }
    public User Owner { get; set; } = null!;

    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<StoreOrder> StoreOrders { get; set; } = new List<StoreOrder>();
    public ICollection<Coupon> Coupons { get; set; } = new List<Coupon>();
}
