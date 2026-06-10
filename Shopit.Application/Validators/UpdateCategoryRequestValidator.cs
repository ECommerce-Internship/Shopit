using FluentValidation;
using Shopit.Application.DTOs.Categories;
public class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(150).WithMessage("Name must not exceed 150 characters.");

        RuleFor(x => x.ParentCategoryId)
            .Must(id => id == null || id > 0)
            .WithMessage("ParentCategoryId must be a positive number if provided.");
    }
}