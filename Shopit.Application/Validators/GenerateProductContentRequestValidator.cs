using FluentValidation;
using Shopit.Application.AI;

namespace Shopit.Application.Validators;

public class GenerateProductContentRequestValidator : AbstractValidator<GenerateProductContentRequest>
{
    public GenerateProductContentRequestValidator()
    {
        RuleFor(x => x.ProductName)
            .NotEmpty().WithMessage("Product name is required.")
            .MaximumLength(200).WithMessage("Product name must not exceed 200 characters.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .MaximumLength(100).WithMessage("Category must not exceed 100 characters.");

        RuleFor(x => x.Specs)
            .NotEmpty().WithMessage("Specifications are required.")
            .MaximumLength(2000).WithMessage("Specifications must not exceed 2000 characters.");
    }
}
