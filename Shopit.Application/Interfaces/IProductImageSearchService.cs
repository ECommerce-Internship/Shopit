using Microsoft.AspNetCore.Http;
using Shopit.Application.DTOs.ImageSearch;

namespace Shopit.Application.Interfaces;

/// <summary>
/// The "search by photo" feature: validate an uploaded image, embed it via
/// Azure Image Retrieval, then return the visually most similar catalog products.
/// </summary>
public interface IProductImageSearchService
{
    /// <summary>
    /// Finds the <paramref name="topK"/> catalog products most visually similar to
    /// <paramref name="image"/>. Throws
    /// <see cref="Shopit.Domain.Exceptions.ValidationException"/> for a missing,
    /// oversized, or non-image upload.
    /// </summary>
    Task<ImageSearchResultResponse> SearchByImageAsync(
        IFormFile image,
        int topK,
        CancellationToken cancellationToken = default);
}
