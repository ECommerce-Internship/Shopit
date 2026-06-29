using System.Security.Claims;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.Chat;

namespace Shopit.API.Controllers;

/// <summary>
/// Chat endpoint that bridges Gemini's function-calling API with the Shopit MCP server's tools.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize]
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
    /// The caller's identity (userId, role) is taken from JWT claims only — never
    /// from the request body — and used to scope and filter tool access.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("Chat")]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChatResponse>> SendMessage(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = User.FindFirstValue(ClaimTypes.Role)!;

        var response = await _chatService.SendMessageAsync(
            request.Message,
            request.ConversationId,
            userId,
            role,
            cancellationToken);

        return Ok(response);
    }
}