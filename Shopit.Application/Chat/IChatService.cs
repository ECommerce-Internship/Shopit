namespace Shopit.Application.Chat;

/// <summary>
/// Bridges a chat conversation between Gemini's function-calling API and the
/// Shopit MCP server's tools, executing any tool calls Gemini requests and
/// returning the final assistant reply.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Sends a message to the assistant on behalf of an authenticated user.
    /// </summary>
    /// <param name="message">The user's message text.</param>
    /// <param name="conversationId">Optional existing conversation id.</param>
    /// <param name="userId">
    /// The caller's user id, taken from JWT claims by the controller. Used to
    /// scope identity-sensitive tool calls (e.g. get_my_orders) and is never
    /// taken from model output.
    /// </param>
    /// <param name="role">
    /// The caller's role (e.g. "Admin" or "Customer"), taken from JWT claims.
    /// Determines which MCP tools are exposed to the model for this request.
    /// </param>
    Task<ChatResponse> SendMessageAsync(
        string message,
        string? conversationId,
        int userId,
        string role,
        CancellationToken cancellationToken = default);
}