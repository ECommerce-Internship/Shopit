using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.AI;

namespace Shopit.API.Controllers;

/// <summary>
/// Generates AI product content.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Authorize(Roles = "Admin")]
[Route("api/v{version:apiVersion}/product-content")]
public class ProductContentController : ControllerBase
{
    private readonly IGeminiService _geminiService;

    public ProductContentController(IGeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    /// <summary>
    /// Generates product description, features, SEO title, and meta description.
    /// </summary>
    [HttpPost("generate")]
    [ProducesResponseType(typeof(ProductContentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ProductContentResponse>> Generate(
        [FromBody] GenerateProductContentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _geminiService.GenerateProductContentAsync(
            request.ProductName,
            request.Category,
            request.Specs,
            request.Price,
            cancellationToken);

        return Ok(result);
    }
}