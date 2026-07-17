namespace Shopit.Application.Rag;

/// <summary>
/// Turns text into an embedding vector for similarity search (SCRUM-166).
/// Implemented against Gemini's embedding API so it stays consistent with the
/// generation model.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Embeds a single piece of text, returning its vector.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);
}
