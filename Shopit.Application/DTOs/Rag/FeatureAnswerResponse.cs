namespace Shopit.Application.DTOs.Rag;

/// <summary>
/// The result of answering a feature question from the docs (SCRUM-166).
/// </summary>
/// <param name="Answer">
/// The grounded answer, or the "not in the documentation" fallback message when
/// <paramref name="Answered"/> is false.
/// </param>
/// <param name="Sources">The doc sections the answer was grounded in (empty when unanswered).</param>
/// <param name="Answered">
/// True if a grounded answer was produced; false if no chunk cleared the
/// relevance threshold and the fallback was returned instead.
/// </param>
public record FeatureAnswerResponse(string Answer, IReadOnlyList<Citation> Sources, bool Answered);
