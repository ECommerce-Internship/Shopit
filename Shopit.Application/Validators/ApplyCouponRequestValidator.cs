using FluentValidation;
using Shopit.Application.DTOs;

namespace Shopit.Application.Validators;

public class ApplyCouponRequestValidator : AbstractValidator<ApplyCouponRequest>
{
    public ApplyCouponRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .WithMessage("Coupon code is required.");
    }
}