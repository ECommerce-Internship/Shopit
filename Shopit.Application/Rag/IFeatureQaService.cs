using Shopit.Application.DTOs.Rag;

namespace Shopit.Application.Rag;

/// <summary>
/// Answers a question about Shopit's features using retrieval-augmented
/// generation (SCRUM-166): retrieves the most relevant doc chunks, and either
/// generates a grounded, cited answer or — when nothing clears the relevance
/// threshold — returns a fallback stating the answer isn't in the docs, rather
/// than letting the model guess.
/// </summary>
public interface IFeatureQaService
{
    /// <summary>Answers a single, self-contained feature question.</summary>
    Task<FeatureAnswerResponse> AskAsync(string question, CancellationToken cancellationToken = default);
}
