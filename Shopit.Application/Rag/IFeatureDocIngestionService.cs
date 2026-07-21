using Shopit.Application.DTOs.Rag;

namespace Shopit.Application.Rag;

/// <summary>
/// Parses the feature documentation Markdown files, chunks them, embeds the
/// chunks and stores the vectors (SCRUM-166). Re-running is idempotent:
/// unchanged chunks (matched by content hash) are not re-embedded.
/// </summary>
public interface IFeatureDocIngestionService
{
    /// <summary>Ingests (or re-ingests) the feature docs, returning a run summary.</summary>
    Task<IngestionResult> ReindexAsync(CancellationToken cancellationToken = default);
}
