using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.ProductAnalytics;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

/// <summary>
/// Records and reports product-interest signals (clicks and time spent) so sellers can
/// spot products drawing attention out of proportion to their sales — e.g. high interest
/// but few purchases, a hint the product may be overpriced.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products/{productId:int}")]
public class ProductAnalyticsController : ControllerBase
{
    private readonly IProductAnalyticsService _analyticsService;

    public ProductAnalyticsController(IProductAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    // Record endpoints are open to anonymous visitors; capture the user id when present.
    private int? GetUserIdOrNull() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() =>
        User.IsInRole("Admin");

    /// <summary>
    /// Records a click/view on a product. Open to anonymous visitors; the user id is
    /// captured when the caller is authenticated.
    /// </summary>
    [HttpPost("clicks")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordClick(int productId, CancellationToken cancellationToken)
    {
        await _analyticsService.RecordClickAsync(productId, GetUserIdOrNull(), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Records the time a user spent on a product page (in milliseconds). Open to anonymous
    /// visitors; the user id is captured when the caller is authenticated.
    /// </summary>
    [HttpPost("time-spent")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordTimeSpent(
        int productId,
        [FromBody] RecordTimeSpentRequest request,
        CancellationToken cancellationToken)
    {
        await _analyticsService.RecordTimeSpentAsync(productId, request, GetUserIdOrNull(), cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Returns click stats (total clicks and unique users) for a product. Restricted to the
    /// product's seller and admins.
    /// </summary>
    [HttpGet("clicks/stats")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(ProductClickStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductClickStatsResponse>> GetClickStats(
        int productId,
        CancellationToken cancellationToken)
    {
        var stats = await _analyticsService.GetClickStatsAsync(productId, GetUserId(), IsAdmin(), cancellationToken);
        return Ok(stats);
    }

    /// <summary>
    /// Returns time-spent stats (total, average, sample count) for a product. Restricted to
    /// the product's seller and admins.
    /// </summary>
    [HttpGet("time-spent/stats")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(ProductTimeSpentStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTimeSpentStatsResponse>> GetTimeSpentStats(
        int productId,
        CancellationToken cancellationToken)
    {
        var stats = await _analyticsService.GetTimeSpentStatsAsync(productId, GetUserId(), IsAdmin(), cancellationToken);
        return Ok(stats);
    }
}
