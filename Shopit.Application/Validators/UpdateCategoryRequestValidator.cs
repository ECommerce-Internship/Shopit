using FluentValidation;
using Shopit.Application.DTOs.Categories;

namespace Shopit.Application.Validators;

public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required.")
            .MaximumLength(150).WithMessage("Category name must not exceed 150 characters.");

        RuleFor(x => x.ParentCategoryId)
            .GreaterThan(0).WithMessage("ParentCategoryId must be a valid ID.")
            .When(x => x.ParentCategoryId.HasValue);
    }
}