using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shopit.Application.DTOs.Rag;
using Shopit.Application.Rag;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Similarity search over the feature-doc chunks (SCRUM-166). The corpus is
/// small (tens of chunks), so this loads all chunks from the database and ranks
/// them by cosine similarity in memory rather than requiring a dedicated vector
/// store or the pgvector extension.
///
/// Chunks are read fresh from the database on each query so that a re-ingestion
/// performed by the API host is immediately reflected in answers served by the
/// MCP host (the two run as separate processes sharing one database).
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<InMemoryVectorStore> _logger;

    public InMemoryVectorStore(AppDbContext db, ILogger<InMemoryVectorStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        var chunks = await _db.DocumentChunks
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (chunks.Count == 0)
        {
            _logger.LogWarning("Feature-doc retrieval ran against an empty corpus — has ingestion been run?");
            return Array.Empty<ScoredChunk>();
        }

        return chunks
            .Select(c => new ScoredChunk(c, CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Cosine similarity of two vectors: dot(a,b) / (|a| * |b|). Returns 0 for a
    /// zero vector or a dimension mismatch, so a malformed stored embedding
    /// simply ranks last rather than throwing.
    /// </summary>
    internal static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length != a.Length)
            return 0d;

        double dot = 0d, magA = 0d, magB = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * (double)a[i];
            magB += b[i] * (double)b[i];
        }

        if (magA == 0d || magB == 0d)
            return 0d;

        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }
}
