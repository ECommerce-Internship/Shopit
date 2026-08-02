using Shopit.Application.DTOs.ImageSearch;

namespace Shopit.Application.Interfaces;

/// <summary>
/// Builds and maintains the visual-search index: for every catalog product that
/// has an image, embed it via Azure Image Retrieval and store the vector.
/// Re-running is idempotent — a product whose image URL is unchanged is skipped
/// (the embedding cache) — and calls are throttled to respect the Azure free
/// (F0) tier limit of 20 transactions/minute. Mirrors the feature-doc ingestion
/// service used for RAG.
/// </summary>
public interface IProductImageIndexingService
{
    Task<ImageIndexResultDto> ReindexAsync(CancellationToken cancellationToken = default);
}
