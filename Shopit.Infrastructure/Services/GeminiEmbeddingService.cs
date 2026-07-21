using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Rag;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Produces text embeddings via the Google Gemini embedding API (SCRUM-166),
/// reusing the same named <c>GeminiClient</c> HttpClient and API key as
/// <see cref="GeminiService"/> so embeddings stay consistent with generation.
/// Upstream failures are translated to <see cref="ExternalServiceException"/>,
/// matching the rest of the Gemini integration.
/// </summary>
public class GeminiEmbeddingService : IEmbeddingService
{
    // Shares the API host's named HttpClient (BaseAddress = Gemini endpoint).
    public const string HttpClientName = GeminiService.HttpClientName;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Gemini:EmbeddingModel"])
            ? "gemini-embedding-001"
            : configuration["Gemini:EmbeddingModel"]!;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        if (string.IsNullOrWhiteSpace(text))
            throw new ExternalServiceException("Cannot embed empty text.");

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{_model}:embedContent");
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(BuildRequestBody(text, _model)),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller cancelled — propagate as-is.
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "Gemini embedding request timed out.");
            throw new ExternalServiceException("The Gemini API request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network failure while calling the Gemini embedding API.");
            throw new ExternalServiceException("Failed to reach the Gemini API.");
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ExtractEmbedding(body);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Gemini embedding API returned {StatusCode}: {Body}",
            (int)response.StatusCode,
            body);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest =>
                new ExternalServiceException("The Gemini API rejected the request. Verify the configured API key."),
            HttpStatusCode.TooManyRequests =>
                new ExternalServiceException("The Gemini API rate limit was exceeded. Please try again later."),
            _ =>
                new ExternalServiceException("The Gemini API is currently unavailable.")
        };
    }

    private float[] ExtractEmbedding(string envelopeJson)
    {
        EmbedResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EmbedResponse>(envelopeJson, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the Gemini embedding response.");
            throw new ExternalServiceException("The Gemini API returned an unreadable response.");
        }

        var values = envelope?.Embedding?.Values;
        if (values is null || values.Length == 0)
            throw new ExternalServiceException("The Gemini API returned an empty embedding.");

        return values;
    }

    private static object BuildRequestBody(string text, string model) => new
    {
        // The API expects the fully-qualified model name in the body ("models/...").
        model = $"models/{model}",
        content = new { parts = new[] { new { text } } }
    };

    private sealed class EmbedResponse
    {
        [JsonPropertyName("embedding")]
        public EmbeddingValues? Embedding { get; set; }
    }

    private sealed class EmbeddingValues
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}
