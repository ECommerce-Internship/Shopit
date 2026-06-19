using FluentValidation;
using Shopit.Application.DTOs.Reviews;

namespace Shopit.Application.Validators;

public class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    public SubmitReviewRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .GreaterThan(0).WithMessage("ProductId must be a valid ID.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.");

        RuleFor(x => x.Comment)
            .MaximumLength(1000).WithMessage("Comment must not exceed 1000 characters.")
            .When(x => x.Comment is not null);
    }
}