namespace Shopit.Application.Chat;

/// <summary>
/// Bridges a chat conversation between Gemini's function-calling API and the
/// Shopit MCP server's tools, executing any tool calls Gemini requests and
/// returning the final assistant reply.
/// </summary>
public interface IChatService
{
    Task<ChatResponse> SendMessageAsync(
        string message,
        string? conversationId,
        CancellationToken cancellationToken = default);
}