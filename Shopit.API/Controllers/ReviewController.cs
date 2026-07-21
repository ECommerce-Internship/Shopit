using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Reviews;
using Shopit.Application.Interfaces;
using System.Security.Claims;
namespace Shopit.API.Controllers;
[ApiController]
[Route("api/v1/reviews")]
public class ReviewController : ControllerBase
{
    private readonly IReviewService _reviewService;
    public ReviewController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }
    [HttpGet("product/{productId}")]
    public async Task<IActionResult> GetByProductId(int productId, [FromQuery] ReviewQueryParameters parameters)
    {
        var result = await _reviewService.GetByProductIdAsync(productId, parameters);
        return Ok(result);
    }
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] ReviewQueryParameters parameters)
    {
        var result = await _reviewService.GetAllReviewsAsync(parameters);
        return Ok(result);
    }
    /// <summary>
    /// Lists reviews currently awaiting moderation (Flagged status) for admin review.
    /// </summary>
    [HttpGet("moderation-queue")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ProductReviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetModerationQueue([FromQuery] ReviewQueryParameters parameters)
    {
        var result = await _reviewService.GetModerationQueueAsync(parameters);
        return Ok(result);
    }
    /// <summary>
    /// Lists flagged or rejected reviews on the current seller's own products, read-only.
    /// </summary>
    [HttpGet("mine/flagged")]
    [Authorize]
    [ProducesResponseType(typeof(ProductReviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyFlaggedReviews([FromQuery] ReviewQueryParameters parameters)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _reviewService.GetFlaggedForSellerAsync(userId, parameters);
        return Ok(result);
    }
    [HttpPost]
    [Authorize]
    [EnableRateLimiting("ReviewModeration")]
    public async Task<IActionResult> Submit([FromBody] SubmitReviewRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _reviewService.SubmitReviewAsync(request, userId);
        return CreatedAtAction(nameof(GetByProductId), new { productId = result.ProductId }, result);
    }
    /// <summary>
    /// Approves a flagged review, making it publicly visible.
    /// </summary>
    [HttpPost("{reviewId}/approve")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Approve(int reviewId)
    {
        var result = await _reviewService.ApproveReviewAsync(reviewId);
        return Ok(result);
    }
    /// <summary>
    /// Rejects a flagged review with an optional reason. The review stays hidden from public view.
    /// </summary>
    [HttpPost("{reviewId}/reject")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(int reviewId, [FromBody] RejectReviewRequest request)
    {
        var result = await _reviewService.RejectReviewAsync(reviewId, request);
        return Ok(result);
    }
    [HttpPut("{reviewId}")]
    [Authorize]
    public async Task<IActionResult> Update(int reviewId, [FromBody] UpdateReviewRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _reviewService.UpdateReviewAsync(reviewId, request, userId);
        return Ok(result);
    }
    [HttpDelete("{reviewId}")]
    [Authorize]
    public async Task<IActionResult> Delete(int reviewId)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _reviewService.DeleteReviewAsync(reviewId, userId);
        return NoContent();
    }
    [HttpDelete("{reviewId}/admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> AdminDelete(int reviewId)
    {
        await _reviewService.AdminDeleteReviewAsync(reviewId);
        return NoContent();
    }
}
