using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shopit.Application.DTOs.ImageSearch;
using Shopit.Application.Interfaces;

namespace Shopit.API.Controllers;

/// <summary>
/// Visual product search ("search by photo") — upload a photo and get the
/// visually most similar catalog products, backed by Azure Computer Vision
/// image embeddings and in-memory cosine similarity.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/image-search")]
public class ImageSearchController : ControllerBase
{
    // Azure Image Analysis 4.0 caps at 20MB/image; bound the request body accordingly.
    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private const int DefaultTopK = 12;

    private readonly IProductImageSearchService _searchService;
    private readonly IProductImageIndexingService _indexingService;

    public ImageSearchController(
        IProductImageSearchService searchService,
        IProductImageIndexingService indexingService)
    {
        _searchService = searchService;
        _indexingService = indexingService;
    }

    /// <summary>
    /// Finds catalog products visually similar to the uploaded photo, ranked by
    /// similarity score (most similar first).
    /// </summary>
    [HttpPost("search")]
    [AllowAnonymous]
    [EnableRateLimiting("ImageSearch")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [ProducesResponseType(typeof(ImageSearchResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ImageSearchResultResponse>> Search(
        IFormFile? image,
        [FromQuery] int topK = DefaultTopK,
        CancellationToken cancellationToken = default)
    {
        // Null/empty/format checks live in the service (throws ValidationException → 400).
        var result = await _searchService.SearchByImageAsync(image!, topK, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Rebuilds the visual-search index from the current catalog: embeds every
    /// product image that is new or changed since the last run. Idempotent and
    /// throttled to respect the embedding provider's rate limit. Admin only.
    /// </summary>
    /// <param name="force">
    /// When <c>true</c>, re-embed every product image even if its URL is unchanged.
    /// Use this after switching the embedding provider (e.g. CLIP → Jina) so the
    /// stored vectors are regenerated with the new model — otherwise the unchanged
    /// URLs are skipped and search compares mismatched vector spaces.
    /// </param>
    [HttpPost("reindex")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ImageIndexResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ImageIndexResultDto>> Reindex(
        [FromQuery] bool force = false,
        CancellationToken cancellationToken = default)
    {
        var result = await _indexingService.ReindexAsync(force, cancellationToken);
        return Ok(result);
    }
}
