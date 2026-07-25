using Shopit.Domain.Enums;

namespace Shopit.Application.DTOs.Coupons;

public record CreateCouponRequest
{
    public string Code { get; init; } = string.Empty;
    public CouponDiscountType DiscountType { get; init; } = CouponDiscountType.Percent;
    public decimal DiscountValue { get; init; }
    public decimal? MinimumOrderAmount { get; init; }
    public int? UsageLimit { get; init; }
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// Store the coupon is scoped to. Sellers must supply a store they own; Admins may
    /// leave it null for a platform-wide coupon or target any store.
    /// </summary>
    public int? StoreId { get; init; }
}
