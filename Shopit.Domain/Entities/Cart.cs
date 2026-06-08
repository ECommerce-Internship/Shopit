using Shopit.Domain.Enums;

namespace Shopit.Domain.Entities;

public class Cart
{
    public int Id { get; set; }
    public CartStatus Status { get; set; } = CartStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int? CouponId { get; set; }
    public Coupon? Coupon { get; set; }

    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}