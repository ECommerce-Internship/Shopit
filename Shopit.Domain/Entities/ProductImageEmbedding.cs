namespace Shopit.Domain.Entities;

/// <summary>
/// The visual embedding of a product's image, used for "search by photo" visual
/// similarity search. Each product has at most one embedding (a 1:1 satellite of
/// <see cref="Product"/>), produced by Azure Computer Vision's Image Retrieval
/// (vectorizeImage) API. Retrieval is a cosine-similarity lookup over the stored
/// vectors, mirroring the text-embedding approach used for feature Q&amp;A
/// (<see cref="DocumentChunk"/>).
/// </summary>
public class ProductImageEmbedding
{
    public int Id { get; set; }

    /// <summary>The product this embedding describes (unique — one embedding per product).</summary>
    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    /// <summary>The image visual embedding vector (Azure Image Retrieval, 1024 dims).</summary>
    public float[] Embedding { get; set; } = Array.Empty<float>();

    /// <summary>
    /// The <see cref="Product.ImageUrl"/> that was embedded. Re-indexing skips a
    /// product whose image URL is unchanged, so this doubles as the embedding cache key.
    /// </summary>
    public string SourceImageUrl { get; set; } = string.Empty;

    /// <summary>The Azure model version that produced <see cref="Embedding"/> (e.g. "2023-04-15").</summary>
    public string ModelVersion { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
