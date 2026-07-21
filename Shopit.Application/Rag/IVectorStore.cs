using Shopit.Application.DTOs.Rag;

namespace Shopit.Application.Rag;

/// <summary>
/// Similarity search over the embedded feature-doc chunks (SCRUM-166). The
/// corpus is small, so implementations may simply load all chunks and rank
/// them by cosine similarity in memory.
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// Returns the <paramref name="topK"/> chunks most similar to
    /// <paramref name="queryEmbedding"/>, ordered by descending score. May
    /// return fewer than <paramref name="topK"/> if the corpus is smaller, or
    /// an empty list if nothing has been ingested yet.
    /// </summary>
    Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default);
}
