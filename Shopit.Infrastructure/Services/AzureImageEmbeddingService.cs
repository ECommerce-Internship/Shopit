using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Interfaces;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Produces image embeddings via Azure Computer Vision's Image Retrieval
/// (vectorizeImage) API — the free-tier ("F0") vision feature that turns an
/// image into a 1024-dim vector for visual similarity search. The raw image
/// bytes are POSTed (not a URL) because catalog images live in local blob
/// storage that Azure cannot reach.
///
/// Upstream failures are translated to <see cref="ExternalServiceException"/>,
/// matching the rest of the codebase's external integrations. HTTP 429 (the F0
/// tier caps at 20 req/min) is retried with exponential backoff, honouring a
/// <c>Retry-After</c> header when present.
/// </summary>
public class AzureImageEmbeddingService : IImageEmbeddingService
{
    public const string HttpClientName = "AzureVisionClient";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const int MaxRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AzureImageEmbeddingService> _logger;
    private readonly string _apiKey;
    private readonly string _apiVersion;
    private readonly string _modelVersion;
    private readonly TimeSpan _retryBaseDelay;

    public AzureImageEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AzureImageEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["AzureVision:ApiKey"] ?? string.Empty;
        var retryBaseMs = int.TryParse(configuration["AzureVision:RetryBaseDelayMs"], out var ms) ? ms : 1000;
        _retryBaseDelay = TimeSpan.FromMilliseconds(Math.Max(0, retryBaseMs));
        _apiVersion = string.IsNullOrWhiteSpace(configuration["AzureVision:ApiVersion"])
            ? "2023-02-01-preview"
            : configuration["AzureVision:ApiVersion"]!;
        _modelVersion = string.IsNullOrWhiteSpace(configuration["AzureVision:ModelVersion"])
            ? "2023-04-15"
            : configuration["AzureVision:ModelVersion"]!;
    }

    public async Task<ImageEmbeddingResult> EmbedImageAsync(Stream image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Azure Computer Vision API key is not configured.");

        if (image is null)
            throw new ExternalServiceException("Cannot embed a null image stream.");

        // Buffer once so the body can be re-sent on a retry (HttpContent is single-use).
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await image.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        if (bytes.Length == 0)
            throw new ExternalServiceException("Cannot embed an empty image.");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        var requestUri = $"computervision/retrieval:vectorizeImage?api-version={_apiVersion}&model-version={_modelVersion}";

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

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
                _logger.LogError(ex, "Azure Image Retrieval request timed out.");
                throw new ExternalServiceException("The Azure Computer Vision request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network failure while calling the Azure Image Retrieval API.");
                throw new ExternalServiceException("Failed to reach the Azure Computer Vision API.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
                {
                    var delay = ComputeBackoff(response, attempt, _retryBaseDelay);
                    _logger.LogWarning(
                        "Azure Image Retrieval rate limit hit (429). Backing off {DelayMs}ms before retry {Attempt}/{MaxRetries}.",
                        delay.TotalMilliseconds, attempt + 1, MaxRetries);
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
    /// Backoff before a 429 retry: honour the server's <c>Retry-After</c> when
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
            "Azure Image Retrieval API returned {StatusCode}: {Body}",
            (int)response.StatusCode,
            body);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ExternalServiceException("The Azure Computer Vision API rejected the request. Verify the configured key and endpoint."),
            HttpStatusCode.BadRequest =>
                new ExternalServiceException("The Azure Computer Vision API rejected the image (unsupported format or too large)."),
            HttpStatusCode.TooManyRequests =>
                new ExternalServiceException("The Azure Computer Vision rate limit was exceeded. Please try again later."),
            _ =>
                new ExternalServiceException("The Azure Computer Vision API is currently unavailable.")
        };
    }

    private ImageEmbeddingResult ExtractEmbedding(string envelopeJson)
    {
        VectorizeResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VectorizeResponse>(envelopeJson, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the Azure Image Retrieval response.");
            throw new ExternalServiceException("The Azure Computer Vision API returned an unreadable response.");
        }

        var vector = envelope?.Vector;
        if (vector is null || vector.Length == 0)
            throw new ExternalServiceException("The Azure Computer Vision API returned an empty embedding.");

        var modelVersion = string.IsNullOrWhiteSpace(envelope!.ModelVersion) ? _modelVersion : envelope.ModelVersion;
        return new ImageEmbeddingResult(vector, modelVersion);
    }

    private sealed class VectorizeResponse
    {
        [JsonPropertyName("modelVersion")]
        public string? ModelVersion { get; set; }

        [JsonPropertyName("vector")]
        public float[]? Vector { get; set; }
    }
}
