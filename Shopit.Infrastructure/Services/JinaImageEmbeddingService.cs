using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Interfaces;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Produces image embeddings via the Jina AI Embeddings API (<c>jina-clip-v2</c>,
/// a multimodal CLIP model) — a hosted, key-based alternative to Azure Computer
/// Vision that works from any host, including free-tier PaaS (Render/Vercel),
/// because it is a single outbound HTTPS call with no local GPU/RAM cost.
///
/// The image bytes are base64-encoded and sent in the request (not a URL) because
/// catalog images may live in storage the Jina service cannot reach. The same
/// model must embed both the indexed catalog images and the query photo, so the
/// model name Jina echoes back is stored as the
/// <see cref="ImageEmbeddingResult.ModelVersion"/> to keep vectors comparable.
///
/// Upstream failures are translated to <see cref="ExternalServiceException"/>,
/// matching the rest of the codebase's external integrations. HTTP 429/503 are
/// retried with exponential backoff, honouring a <c>Retry-After</c> header.
/// </summary>
public class JinaImageEmbeddingService : IImageEmbeddingService
{
    public const string HttpClientName = "JinaEmbeddingClient";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string DefaultModel = "jina-clip-v2";
    private const int MaxRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JinaImageEmbeddingService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly TimeSpan _retryBaseDelay;

    public JinaImageEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<JinaImageEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Jina:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Jina:Model"])
            ? DefaultModel
            : configuration["Jina:Model"]!;
        var retryBaseMs = int.TryParse(configuration["Jina:RetryBaseDelayMs"], out var ms) ? ms : 1000;
        _retryBaseDelay = TimeSpan.FromMilliseconds(Math.Max(0, retryBaseMs));
    }

    public async Task<ImageEmbeddingResult> EmbedImageAsync(Stream image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Jina API key is not configured.");

        if (image is null)
            throw new ExternalServiceException("Cannot embed a null image stream.");

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await image.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (bytes.Length == 0)
            throw new ExternalServiceException("Cannot embed an empty image.");

        // Jina accepts a base64-encoded image (no data-URI prefix) in the input.
        var payload = JsonSerializer.Serialize(new JinaRequest
        {
            Model = _model,
            Input = new[] { new JinaInput { Image = Convert.ToBase64String(bytes) } }
        }, SerializerOptions);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings");
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

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
                _logger.LogError(ex, "Jina embeddings request timed out.");
                throw new ExternalServiceException("The Jina request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network failure while calling the Jina Embeddings API.");
                throw new ExternalServiceException("Failed to reach the Jina Embeddings API.");
            }

            using (response)
            {
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.ServiceUnavailable;
                if (transient && attempt < MaxRetries)
                {
                    var delay = ComputeBackoff(response, attempt, _retryBaseDelay);
                    _logger.LogWarning(
                        "Jina returned {StatusCode}. Backing off {DelayMs}ms before retry {Attempt}/{MaxRetries}.",
                        (int)response.StatusCode, delay.TotalMilliseconds, attempt + 1, MaxRetries);
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                await EnsureSuccessAsync(response, cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return ExtractEmbedding(body);
            }
        }
    }

    /// <summary>
    /// Backoff before a 429/503 retry: honour the server's <c>Retry-After</c> when
    /// present, otherwise exponential on the configured base delay (base, base×2, base×4).
    /// </summary>
    private static TimeSpan ComputeBackoff(HttpResponseMessage response, int attempt, TimeSpan baseDelay)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        if (retryAfter is { } wait && wait > TimeSpan.Zero)
            return wait;

        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Jina Embeddings API returned {StatusCode}: {Body}",
            (int)response.StatusCode,
            body);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ExternalServiceException("The Jina API rejected the request. Verify the configured key."),
            HttpStatusCode.PaymentRequired =>
                new ExternalServiceException("The Jina API key is out of quota. Top up or use a different key."),
            HttpStatusCode.BadRequest =>
                new ExternalServiceException("The Jina API rejected the image (unsupported format or too large)."),
            HttpStatusCode.TooManyRequests =>
                new ExternalServiceException("The Jina rate limit was exceeded. Please try again later."),
            _ =>
                new ExternalServiceException("The Jina Embeddings API is currently unavailable.")
        };
    }

    private ImageEmbeddingResult ExtractEmbedding(string json)
    {
        JinaResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<JinaResponse>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the Jina Embeddings response.");
            throw new ExternalServiceException("The Jina API returned an unreadable response.");
        }

        var vector = envelope?.Data is { Length: > 0 } data ? data[0].Embedding : null;
        if (vector is null || vector.Length == 0)
            throw new ExternalServiceException("The Jina API returned an empty embedding.");

        var model = string.IsNullOrWhiteSpace(envelope!.Model) ? _model : envelope.Model;
        return new ImageEmbeddingResult(vector, model);
    }

    private sealed class JinaRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public JinaInput[] Input { get; set; } = Array.Empty<JinaInput>();
    }

    private sealed class JinaInput
    {
        [JsonPropertyName("image")]
        public string? Image { get; set; }
    }

    private sealed class JinaResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("data")]
        public JinaEmbedding[]? Data { get; set; }
    }

    private sealed class JinaEmbedding
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
