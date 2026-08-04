using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.DTOs.ImageSearch;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Builds/refreshes the visual-search index (mirrors <see cref="FeatureDocIngestionService"/>):
/// enumerate products with an image → download → embed → upsert. Re-running is
/// idempotent — a product whose <see cref="Product.ImageUrl"/> is unchanged keeps
/// its stored vector and spends no Azure transaction (the embedding cache).
/// Embeddings for products that lost their image or were deleted are removed.
///
/// Azure's free (F0) tier allows only 20 transactions/minute, so a configurable
/// delay is inserted between embedding calls. A single unreachable image or Azure
/// error is logged and counted as a failure rather than aborting the whole run.
/// </summary>
public class ProductImageIndexingService : IProductImageIndexingService
{
    private readonly AppDbContext _db;
    private readonly IImageEmbeddingService _embeddingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ProductImageIndexingService> _logger;
    private readonly TimeSpan _throttleDelay;

    public ProductImageIndexingService(
        AppDbContext db,
        IImageEmbeddingService embeddingService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ProductImageIndexingService> logger)
    {
        _db = db;
        _embeddingService = embeddingService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // 20 req/min ⇒ one call every ~3s. Default 3100ms leaves headroom; overridable.
        var delayMs = int.TryParse(configuration["AzureVision:IndexDelayMs"], out var ms) ? ms : 3100;
        _throttleDelay = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));
    }

    public async Task<ImageIndexResultDto> ReindexAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var products = await _db.Products
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.ImageUrl != null && p.ImageUrl != "")
            .Select(p => new { p.Id, p.ImageUrl })
            .ToListAsync(cancellationToken);

        var existing = await _db.ProductImageEmbeddings.ToListAsync(cancellationToken);
        var existingByProduct = existing.ToDictionary(e => e.ProductId);
        var seenProductIds = new HashSet<int>();

        int embedded = 0, skipped = 0, failed = 0;
        var httpClient = _httpClientFactory.CreateClient();
        // Many image CDNs reject requests without a User-Agent (HTTP 403), so
        // identify ourselves when downloading catalog images.
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Shopit-ImageIndexer/1.0");

        foreach (var product in products)
        {
            seenProductIds.Add(product.Id);
            var imageUrl = product.ImageUrl!;

            existingByProduct.TryGetValue(product.Id, out var row);
            if (!force && row is not null && row.SourceImageUrl == imageUrl)
            {
                // Image unchanged since last run — keep the stored vector, spend no
                // embedding call. Bypassed when force=true (e.g. after switching the
                // embedding provider, where the stored vector is from a different model).
                skipped++;
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = await httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException or TaskCanceledException
                                       && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Skipping product {ProductId}: failed to download image {ImageUrl}.", product.Id, imageUrl);
                failed++;
                continue;
            }

            ImageEmbeddingResult result;
            try
            {
                await using var stream = new MemoryStream(bytes);
                result = await _embeddingService.EmbedImageAsync(stream, cancellationToken);
            }
            catch (ExternalServiceException ex)
            {
                _logger.LogWarning(ex, "Skipping product {ProductId}: embedding provider failed to embed image.", product.Id);
                failed++;
                continue;
            }

            if (row is null)
            {
                _db.ProductImageEmbeddings.Add(new ProductImageEmbedding
                {
                    ProductId = product.Id,
                    Embedding = result.Vector,
                    SourceImageUrl = imageUrl,
                    ModelVersion = result.ModelVersion,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Embedding = result.Vector;
                row.SourceImageUrl = imageUrl;
                row.ModelVersion = result.ModelVersion;
                row.UpdatedAt = DateTime.UtcNow;
            }

            embedded++;

            // Throttle to respect the Azure free (F0) tier's 20 req/min ceiling.
            if (_throttleDelay > TimeSpan.Zero)
                await Task.Delay(_throttleDelay, cancellationToken);
        }

        // Drop embeddings for products that no longer have an image (or were deleted).
        var stale = existing.Where(e => !seenProductIds.Contains(e.ProductId)).ToList();
        if (stale.Count > 0)
            _db.ProductImageEmbeddings.RemoveRange(stale);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Visual-search re-index complete: {Embedded} embedded, {Skipped} unchanged, {Failed} failed, {Removed} removed.",
            embedded, skipped, failed, stale.Count);

        return new ImageIndexResultDto(embedded, skipped, failed, stale.Count);
    }
}
