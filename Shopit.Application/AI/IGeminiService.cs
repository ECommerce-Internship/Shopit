namespace Shopit.Application.AI;

public interface IGeminiService
{
    Task<ProductContentResponse> GenerateProductContentAsync(
        string productName,
        string category,
        string specs,
        decimal price,
        CancellationToken cancellationToken = default);
}