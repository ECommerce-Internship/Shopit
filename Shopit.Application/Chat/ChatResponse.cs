namespace Shopit.Application.Chat;

/// <summary>
/// Result of a single chat turn.
/// </summary>
/// <param name="Reply">The assistant's final text reply after any tool calls.</param>
/// <param name="ConversationId">The identifier for this conversation, for use in follow-up requests.</param>
public record ChatResponse(string Reply, string ConversationId);