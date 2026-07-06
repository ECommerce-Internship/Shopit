using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shopit.Application.AI;

namespace Shopit.API.Controllers;

/// <summary>
/// AI-powered product content generation.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AiController : ControllerBase
{
    private readonly IGeminiService _geminiService;
    private readonly IValidator<GenerateProductContentRequest> _validator;

    public AiController(
        IGeminiService geminiService,
        IValidator<GenerateProductContentRequest> validator)
    {
        _geminiService = geminiService;
        _validator = validator;
    }

    /// <summary>
    /// Generates product marketing content (description, 5 features, SEO title, meta description)
    /// from the supplied product information.
    /// </summary>
    [HttpPost("generate-content")]
    [EnableRateLimiting("GeminiContentGeneration")]
    [ProducesResponseType(typeof(ProductContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ProductContentResponse>> GenerateContent(
        [FromBody] GenerateProductContentRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(validation.Errors.Select(e => e.ErrorMessage));

        var content = await _geminiService.GenerateProductContentAsync(
            request.ProductName,
            request.Category,
            request.Specs,
            cancellationToken);

        return Ok(content);
    }
}
