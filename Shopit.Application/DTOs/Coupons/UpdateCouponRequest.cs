using Shopit.Domain.Enums;

namespace Shopit.Application.DTOs.Coupons;

public record UpdateCouponRequest
{
    public CouponDiscountType DiscountType { get; init; } = CouponDiscountType.Percent;
    public decimal DiscountValue { get; init; }
    public decimal? MinimumOrderAmount { get; init; }
    public int? UsageLimit { get; init; }
    public DateTime? ExpiresAt { get; init; }
}
