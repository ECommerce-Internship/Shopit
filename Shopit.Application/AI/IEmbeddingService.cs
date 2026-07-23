namespace Shopit.Application.AI;

/// <summary>
/// Generates a dense embedding vector for a given text using Gemini text-embedding-004.
/// </summary>
public interface IEmbeddingService
{
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default);
}