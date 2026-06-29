namespace Shopit.Application.Chat;

/// <summary>
/// Incoming request for a single chat turn.
/// </summary>
/// <param name="Message">The user's message text.</param>
/// <param name="ConversationId">
/// Optional identifier for an existing conversation. If omitted, a new conversation is started.
/// </param>
public record ChatRequest(string Message, string? ConversationId = null);