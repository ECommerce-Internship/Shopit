using FluentValidation;
using Shopit.Application.DTOs.ProductAnalytics;

namespace Shopit.Application.Validators;

public class RecordTimeSpentRequestValidator : AbstractValidator<RecordTimeSpentRequest>
{
    // A single product-page dwell time. Reject non-positive values and anything longer than
    // a day, which signals a client bug or tampering rather than genuine interest.
    private const long MaxDurationMs = 24L * 60 * 60 * 1000;

    public RecordTimeSpentRequestValidator()
    {
        RuleFor(x => x.DurationMs)
            .GreaterThan(0).WithMessage("DurationMs must be greater than 0.")
            .LessThanOrEqualTo(MaxDurationMs).WithMessage("DurationMs must not exceed 24 hours.");
    }
}
