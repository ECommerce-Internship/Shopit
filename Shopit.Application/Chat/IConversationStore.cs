using System.Text.Json.Nodes;

namespace Shopit.Application.Chat;

/// <summary>
/// Persists chat conversation history per user (SCRUM-109), so a follow-up
/// message in the same conversation has context from prior turns.
///
/// History is represented as a JsonArray in the same shape ChatService
/// already builds internally for Gemini's "contents" field (alternating
/// user/model entries, including function calls and responses), so no
/// translation is needed between what's stored and what's sent to Gemini.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Returns the stored history for a conversation, scoped to the given
    /// user. Returns null if there is no history yet, if it has expired, or
    /// if the conversationId does not belong to this user — all three cases
    /// are treated identically (no history available), so an attempt to load
    /// another user's conversation looks the same as a fresh one.
    /// </summary>
    Task<JsonArray?> GetAsync(string conversationId, int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores (overwriting any previous value) the history for a conversation,
    /// scoped to the given user, with a configurable TTL.
    /// </summary>
    Task SaveAsync(string conversationId, int userId, JsonArray history, CancellationToken cancellationToken = default);
}