using Shopit.Application.DTOs.ImageSearch;

namespace Shopit.Application.Interfaces;

/// <summary>
/// Similarity search over the stored product image embeddings. The prototype
/// catalog is small (dozens to a few hundred products), so implementations may
/// load all embeddings and rank them by cosine similarity in memory rather than
/// requiring a dedicated vector store or the pgvector extension — mirroring
/// <see cref="Shopit.Application.Rag.IVectorStore"/>.
/// </summary>
public interface IImageVectorStore
{
    /// <summary>
    /// Returns the <paramref name="topK"/> products most visually similar to
    /// <paramref name="queryEmbedding"/>, ordered by descending score. Deleted
    /// products are excluded. May return fewer than <paramref name="topK"/> if the
    /// index is smaller, or an empty list if nothing has been indexed yet.
    /// </summary>
    Task<IReadOnlyList<ImageSearchMatchResponse>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);
}
