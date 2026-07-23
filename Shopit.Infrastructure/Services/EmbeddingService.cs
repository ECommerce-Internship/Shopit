using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.AI;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Generates text embeddings using the Gemini text-embedding-004 model.
/// Reuses the same GeminiClient HttpClient registered in Program.cs.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<EmbeddingService> _logger;
    private readonly string _apiKey;
    private const string Model = "gemini-embedding-001";

    public EmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<EmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        var client = _httpClientFactory.CreateClient(GeminiService.HttpClientName);

        var body = new
        {
            model = $"models/{Model}",
            content = new { parts = new[] { new { text } } },
            taskType = "RETRIEVAL_DOCUMENT"
        };

        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/models/{Model}:embedContent?key={_apiKey}")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ExternalServiceException("Gemini embedding rate limit exceeded.");

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Gemini embedding API returned {Status}: {Body}", (int)response.StatusCode, err);
            throw new ExternalServiceException("The Gemini embedding API is currently unavailable.");
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        var result = JsonSerializer.Deserialize<EmbedResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var values = result?.Embedding?.Values;
        if (values is null || values.Length == 0)
            throw new ExternalServiceException("Gemini returned an empty embedding.");

        return values;
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embedding")]
        public EmbeddingValues? Embedding { get; set; }
    }

    private sealed class EmbeddingValues
    {
        [JsonPropertyName("values")]
        public float[] Values { get; set; } = [];
    }
}