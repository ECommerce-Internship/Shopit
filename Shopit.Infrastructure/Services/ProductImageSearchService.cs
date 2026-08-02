using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shopit.Application.DTOs.ImageSearch;
using Shopit.Application.Interfaces;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// The "search by photo" feature: validate the uploaded query image, embed it
/// via Azure Image Retrieval, then rank the catalog by visual similarity.
/// </summary>
public class ProductImageSearchService : IProductImageSearchService
{
    // Azure Image Analysis 4.0 accepts up to 20MB per image; reject larger uploads
    // before spending an Azure transaction on a request that would fail anyway.
    private const long MaxImageSizeBytes = 20 * 1024 * 1024;
    private const int MaxTopK = 50;

    private readonly IImageEmbeddingService _embeddingService;
    private readonly IImageVectorStore _vectorStore;
    private readonly ILogger<ProductImageSearchService> _logger;

    public ProductImageSearchService(
        IImageEmbeddingService embeddingService,
        IImageVectorStore vectorStore,
        ILogger<ProductImageSearchService> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    public async Task<ImageSearchResultResponse> SearchByImageAsync(
        IFormFile image,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (image is null || image.Length == 0)
            throw new ValidationException("An image file is required.");

        if (image.Length > MaxImageSizeBytes)
            throw new ValidationException("Image must not exceed 20MB.");

        if (!image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("The uploaded file must be an image.");

        var effectiveTopK = Math.Clamp(topK, 1, MaxTopK);

        await using var stream = image.OpenReadStream();
        var embedding = await _embeddingService.EmbedImageAsync(stream, cancellationToken);

        _logger.LogInformation(
            "Visual search: embedded query image ({Bytes} bytes) into a {Dimensions}-dim vector; searching top {TopK}.",
            image.Length, embedding.Vector.Length, effectiveTopK);

        var matches = await _vectorStore.SearchAsync(embedding.Vector, effectiveTopK, cancellationToken);
        return new ImageSearchResultResponse(matches);
    }
}
