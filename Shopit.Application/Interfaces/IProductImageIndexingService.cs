using Shopit.Application.DTOs.ImageSearch;

namespace Shopit.Application.Interfaces;

/// <summary>
/// Builds and maintains the visual-search index: for every catalog product that
/// has an image, embed it via the active <see cref="IImageEmbeddingService"/>
/// provider and store the vector. Re-running is idempotent — a product whose image
/// URL is unchanged is skipped (the embedding cache) — and calls are throttled to
/// respect the embedding provider's rate limit. Mirrors the feature-doc ingestion
/// service used for RAG.
/// </summary>
public interface IProductImageIndexingService
{
    /// <param name="force">
    /// When <c>true</c>, bypass the "image URL unchanged" skip-cache and re-embed
    /// every product. Required after switching the embedding provider (e.g. CLIP →
    /// Jina): the stored vectors come from a different model and are not comparable
    /// to the new provider's query vectors, so they must all be regenerated even
    /// though the source image URLs have not changed.
    /// </param>
    Task<ImageIndexResultDto> ReindexAsync(bool force = false, CancellationToken cancellationToken = default);
}
