namespace Shopit.Application.DTOs.Rag;

/// <summary>
/// A source the answer was grounded in (SCRUM-166), so the user can see which
/// feature doc(s) an answer came from.
/// </summary>
/// <param name="FeatureName">The feature the source section describes.</param>
/// <param name="Section">The specific section heading within that feature doc.</param>
/// <param name="Audience">Which audience the doc targets: "customer" or "seller".</param>
/// <param name="SourceFile">Repo-relative path of the source Markdown file.</param>
public record Citation(string FeatureName, string Section, string Audience, string SourceFile);
