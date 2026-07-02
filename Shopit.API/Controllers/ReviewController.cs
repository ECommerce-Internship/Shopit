using Microsoft.AspNetCore.Authorization;
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

    [HttpPost]
    [Authorize]
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll([FromQuery] ReviewQueryParameters parameters)
    {
        var result = await _reviewService.GetAllReviewsAsync(parameters);
        return Ok(result);
    }
    public async Task<IActionResult> Submit([FromBody] SubmitReviewRequest request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _reviewService.SubmitReviewAsync(request, userId);
        return CreatedAtAction(nameof(GetByProductId), new { productId = result.ProductId }, result);
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