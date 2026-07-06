namespace Shopit.Application.AI;

/// <summary>
/// Generates product marketing content (description, features, SEO title, meta description)
/// using the Google Gemini API.
/// </summary>
public interface IGeminiService
{
    Task<ProductContentResponse> GenerateProductContentAsync(
        string productName,
        string category,
        string specs,
        CancellationToken cancellationToken = default);
}
