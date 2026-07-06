using FluentValidation;
using Shopit.Application.DTOs;

namespace Shopit.Application.Validators;

public class UpdateThresholdRequestValidator : AbstractValidator<UpdateThresholdRequest>
{
    public UpdateThresholdRequestValidator()
    {
        RuleFor(x => x.Threshold)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Threshold cannot be negative.");
    }
}