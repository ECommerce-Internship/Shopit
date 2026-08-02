namespace Shopit.Application.DTOs.ImageSearch;

/// <summary>The ranked results of a visual product search, most similar first.</summary>
public record ImageSearchResultResponse(IReadOnlyList<ImageSearchMatchResponse> Matches);
