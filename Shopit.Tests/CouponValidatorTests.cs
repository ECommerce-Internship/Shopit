using FluentAssertions;
using Shopit.Application.DTOs.Coupons;
using Shopit.Application.Validators;
using Shopit.Domain.Enums;
using Xunit;

namespace Shopit.Tests;

public class CouponValidatorTests
{
    private readonly CreateCouponRequestValidator _createValidator = new();
    private readonly UpdateCouponRequestValidator _updateValidator = new();

    private static CreateCouponRequest ValidCreate(DateTime? expiresAt) => new()
    {
        Code = "SAVE10",
        DiscountType = CouponDiscountType.Percent,
        DiscountValue = 10,
        ExpiresAt = expiresAt
    };

    private static UpdateCouponRequest ValidUpdate(DateTime? expiresAt) => new()
    {
        DiscountType = CouponDiscountType.Percent,
        DiscountValue = 10,
        ExpiresAt = expiresAt
    };

    [Fact]
    public void CreateCoupon_ExpiresInPast_FailsValidation()
    {
        var result = _createValidator.Validate(ValidCreate(DateTime.UtcNow.AddDays(-1)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateCouponRequest.ExpiresAt));
    }

    [Fact]
    public void CreateCoupon_ExpiresInFuture_PassesValidation()
    {
        var result = _createValidator.Validate(ValidCreate(DateTime.UtcNow.AddDays(1)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateCoupon_NoExpiry_PassesValidation()
    {
        var result = _createValidator.Validate(ValidCreate(expiresAt: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateCoupon_ExpiresInPast_FailsValidation()
    {
        var result = _updateValidator.Validate(ValidUpdate(DateTime.UtcNow.AddDays(-1)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateCouponRequest.ExpiresAt));
    }

    [Fact]
    public void UpdateCoupon_ExpiresInFuture_PassesValidation()
    {
        var result = _updateValidator.Validate(ValidUpdate(DateTime.UtcNow.AddDays(1)));

        result.IsValid.Should().BeTrue();
    }
}
