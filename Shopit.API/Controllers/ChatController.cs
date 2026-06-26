using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.Chat;

namespace Shopit.API.Controllers;

/// <summary>
/// Chat endpoint that bridges Gemini's function-calling API with the Shopit MCP server's tools.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Sends a message to the assistant. If no conversationId is supplied, a new
    /// conversation is started and its id is returned for use in follow-up requests.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChatResponse>> SendMessage(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new[] { "Message is required." });

        var response = await _chatService.SendMessageAsync(
            request.Message,
            request.ConversationId,
            cancellationToken);

        return Ok(response);
    }
}