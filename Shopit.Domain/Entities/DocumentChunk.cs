namespace Shopit.Domain.Entities;

/// <summary>
/// A single embedded slice of a feature documentation Markdown file, used as the
/// retrieval corpus for feature Q&amp;A (SCRUM-166). Each chunk corresponds to one
/// "##" section of a doc, carries the metadata needed to cite it, and stores the
/// embedding vector so retrieval is a cosine-similarity lookup.
/// </summary>
public class DocumentChunk
{
    public int Id { get; set; }

    /// <summary>Feature title taken from the doc's top-level "#" heading (e.g. "Product Reviews").</summary>
    public string FeatureName { get; set; } = string.Empty;

    /// <summary>The "##" section heading this chunk came from (e.g. "Who can use it").</summary>
    public string Section { get; set; } = string.Empty;

    /// <summary>Which audience folder the source doc lives under: "customer" or "seller".</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>Repo-relative path of the source Markdown file, used for citations.</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// The embedded text: the section body prefixed with a "Feature &gt; Section"
    /// breadcrumb so the chunk is self-contained for similarity search.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the raw source section. Lets re-ingestion skip re-embedding
    /// chunks whose content is unchanged (the embedding cache).
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>The embedding vector for <see cref="Content"/> (Gemini text embeddings, 768 dims).</summary>
    public float[] Embedding { get; set; } = Array.Empty<float>();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
