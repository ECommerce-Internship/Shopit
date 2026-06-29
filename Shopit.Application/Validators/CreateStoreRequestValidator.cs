using FluentValidation;
using Shopit.Application.DTOs.Stores;

namespace Shopit.Application.Validators;

public class CreateStoreRequestValidator : AbstractValidator<CreateStoreRequest>
{
    public CreateStoreRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Store name is required.")
            .MaximumLength(100).WithMessage("Store name must be at most 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Store description must be at most 1000 characters.");
    }
}
