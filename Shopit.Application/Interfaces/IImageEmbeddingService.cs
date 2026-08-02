namespace Shopit.Application.Interfaces;

/// <summary>
/// Turns an image into a visual embedding vector for similarity search.
/// Implemented against Azure Computer Vision's Image Retrieval
/// (vectorizeImage) API. The same model must embed both the indexed catalog
/// images and the query photo, so matches are comparable.
/// </summary>
public interface IImageEmbeddingService
{
    /// <summary>
    /// Embeds a single image, returning its vector. The stream is sent as raw
    /// bytes to Azure. Upstream failures surface as
    /// <see cref="Shopit.Domain.Exceptions.ExternalServiceException"/>.
    /// </summary>
    /// <returns>The embedding vector and the model version that produced it.</returns>
    Task<ImageEmbeddingResult> EmbedImageAsync(Stream image, CancellationToken cancellationToken = default);
}

/// <summary>The vector for an image plus the Azure model version that produced it.</summary>
public record ImageEmbeddingResult(float[] Vector, string ModelVersion);
