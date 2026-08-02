using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shopit.Application.DTOs.ImageSearch;
using Shopit.Application.Interfaces;
using Shopit.Application.Products.DTOs;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Visual similarity search over the stored product image embeddings. The
/// prototype catalog is small (dozens to a few hundred products), so this loads
/// all embeddings from the database and ranks them by cosine similarity in
/// memory rather than requiring a dedicated vector store or the pgvector
/// extension — mirroring <see cref="InMemoryVectorStore"/> used for feature Q&amp;A.
///
/// Embeddings are read fresh on each query so a re-index is immediately
/// reflected in results.
/// </summary>
public class InMemoryImageVectorStore : IImageVectorStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<InMemoryImageVectorStore> _logger;

    public InMemoryImageVectorStore(AppDbContext db, ILogger<InMemoryImageVectorStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ImageSearchMatchResponse>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        // Exclude soft-deleted products; pull the navigations ProductResponse needs.
        var candidates = await _db.ProductImageEmbeddings
            .AsNoTracking()
            .Include(e => e.Product).ThenInclude(p => p.Category)
            .Include(e => e.Product).ThenInclude(p => p.Store)
            .Include(e => e.Product).ThenInclude(p => p.Inventory)
            .Include(e => e.Product).ThenInclude(p => p.Reviews)
            .Where(e => !e.Product.IsDeleted)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogWarning("Visual search ran against an empty index — has the catalog been re-indexed?");
            return Array.Empty<ImageSearchMatchResponse>();
        }

        return candidates
            .Select(e => new ImageSearchMatchResponse(
                MapToResponse(e.Product),
                InMemoryVectorStore.CosineSimilarity(queryEmbedding, e.Embedding)))
            .OrderByDescending(m => m.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Maps a loaded <see cref="Shopit.Domain.Entities.Product"/> to the same
    /// <see cref="ProductResponse"/> shape the products API returns, so visual-search
    /// hits render identically to normal product listings.
    /// </summary>
    private static ProductResponse MapToResponse(Shopit.Domain.Entities.Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        Sku = p.SKU,
        ImageUrl = p.ImageUrl,
        CategoryId = p.CategoryId,
        CategoryName = p.Category?.Name ?? string.Empty,
        StoreId = p.StoreId,
        StoreName = p.Store?.Name ?? string.Empty,
        StoreSlug = p.Store?.Slug ?? string.Empty,
        StockQuantity = p.Inventory?.Quantity ?? 0,
        AverageRating = p.Reviews.Count != 0 ? p.Reviews.Average(r => r.Rating) : 0,
        ReviewCount = p.Reviews.Count,
        CreatedAt = p.CreatedAt
    };
}
