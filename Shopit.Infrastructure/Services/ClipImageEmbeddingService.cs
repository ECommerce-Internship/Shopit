using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Interfaces;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Produces image embeddings from the self-hosted CLIP sidecar (the Python
/// service under <c>clip-service/</c>, reachable in Docker at
/// <c>http://clip:8000</c>). A local, key-free alternative to Azure Computer
/// Vision / Hugging Face: the sidecar wraps a SentenceTransformer CLIP model and
/// returns a vector for the POSTed image bytes. No API key, no per-call cost, no
/// external rate limits.
///
/// The same model must embed both the indexed catalog images and the query photo,
/// so the model name the sidecar echoes back is stored as the
/// <see cref="ImageEmbeddingResult.ModelVersion"/> to keep vectors comparable.
///
/// Upstream failures are translated to <see cref="ExternalServiceException"/>,
/// matching the rest of the codebase's external integrations. A 503 (the sidecar
/// still warming up) is retried with exponential backoff.
/// </summary>
public class ClipImageEmbeddingService : IImageEmbeddingService
{
    public const string HttpClientName = "ClipEmbeddingClient";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const int MaxRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClipImageEmbeddingService> _logger;
    private readonly TimeSpan _retryBaseDelay;

    public ClipImageEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ClipImageEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        var retryBaseMs = int.TryParse(configuration["ClipEmbedding:RetryBaseDelayMs"], out var ms) ? ms : 1000;
        _retryBaseDelay = TimeSpan.FromMilliseconds(Math.Max(0, retryBaseMs));
    }

    public async Task<ImageEmbeddingResult> EmbedImageAsync(Stream image, CancellationToken cancellationToken = default)
    {
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

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "embed");
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
                _logger.LogError(ex, "CLIP embedding request timed out.");
                throw new ExternalServiceException("The CLIP embedding request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network failure while calling the CLIP embedding sidecar.");
                throw new ExternalServiceException("Failed to reach the CLIP embedding service.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < MaxRetries)
                {
                    var delay = ComputeBackoff(response, attempt, _retryBaseDelay);
                    _logger.LogWarning(
                        "CLIP sidecar unavailable (503). Backing off {DelayMs}ms before retry {Attempt}/{MaxRetries}.",
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
    /// Backoff before a 503 retry: honour a <c>Retry-After</c> when present,
    /// otherwise exponential on the configured base delay (base, base×2, base×4).
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
            "CLIP embedding sidecar returned {StatusCode}: {Body}",
            (int)response.StatusCode,
            body);

        throw response.StatusCode switch
        {
            HttpStatusCode.BadRequest =>
                new ExternalServiceException("The CLIP service rejected the image (unsupported or corrupt)."),
            _ =>
                new ExternalServiceException("The CLIP embedding service is currently unavailable.")
        };
    }

    private ImageEmbeddingResult ExtractEmbedding(string json)
    {
        EmbedResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EmbedResponse>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the CLIP sidecar response.");
            throw new ExternalServiceException("The CLIP service returned an unreadable response.");
        }

        var vector = envelope?.Vector;
        if (vector is null || vector.Length == 0)
            throw new ExternalServiceException("The CLIP service returned an empty embedding.");

        var model = string.IsNullOrWhiteSpace(envelope!.Model) ? "clip" : envelope.Model;
        return new ImageEmbeddingResult(vector, model);
    }

    private sealed class EmbedResponse
    {
        [JsonPropertyName("vector")]
        public float[]? Vector { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }
    }
}
