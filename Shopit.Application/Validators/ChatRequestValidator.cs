using FluentValidation;
using Shopit.Application.Chat;

namespace Shopit.Application.Validators;

/// <summary>
/// Validates incoming chat requests (SCRUM-110):
///   - Message is required and capped at 2,000 characters, since the chat
///     endpoint calls Gemini on every request and may trigger multiple tool
///     calls per turn, making unbounded input a direct cost/abuse risk.
///   - ConversationId, if supplied, must be a valid GUID. ChatService always
///     generates conversation ids server-side via Guid.NewGuid().ToString(),
///     so any client-supplied value is expected to match that format.
/// </summary>
public class ChatRequestValidator : AbstractValidator<ChatRequest>
{
    private const int MaxMessageLength = 2000;

    public ChatRequestValidator()
    {
        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Message is required.")
            .MaximumLength(MaxMessageLength).WithMessage($"Message must not exceed {MaxMessageLength} characters.");

        RuleFor(x => x.ConversationId)
            .Must(id => Guid.TryParse(id, out _))
            .When(x => x.ConversationId is not null)
            .WithMessage("ConversationId must be a valid GUID.");
    }
}