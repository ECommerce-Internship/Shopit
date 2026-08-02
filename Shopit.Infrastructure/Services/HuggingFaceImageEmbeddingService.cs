using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Interfaces;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Produces image embeddings via the Hugging Face Inference API's
/// <c>feature-extraction</c> pipeline — a free, hosted alternative to Azure
/// Computer Vision's Image Retrieval. The default model
/// (<c>sentence-transformers/clip-ViT-B-32</c>) turns an image into a 512-dim
/// CLIP vector for visual similarity search. Raw image bytes are POSTed (not a
/// URL) because catalog images live in local blob storage the service cannot reach.
///
/// The same model must embed both the indexed catalog images and the query photo,
/// so the configured model name is returned as the <see cref="ImageEmbeddingResult.ModelVersion"/>
/// to keep stored and query vectors comparable.
///
/// Upstream failures are translated to <see cref="ExternalServiceException"/>,
/// matching the rest of the codebase's external integrations. HTTP 429 (rate limit)
/// and 503 (serverless cold-start "model loading") are retried with exponential
/// backoff, honouring a <c>Retry-After</c> header when present.
/// </summary>
public class HuggingFaceImageEmbeddingService : IImageEmbeddingService
{
    public const string HttpClientName = "HuggingFaceVisionClient";

    private const string DefaultModel = "sentence-transformers/clip-ViT-B-32";
    private const int MaxRetries = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HuggingFaceImageEmbeddingService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly TimeSpan _retryBaseDelay;

    public HuggingFaceImageEmbeddingService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HuggingFaceImageEmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["HuggingFace:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["HuggingFace:Model"])
            ? DefaultModel
            : configuration["HuggingFace:Model"]!;
        var retryBaseMs = int.TryParse(configuration["HuggingFace:RetryBaseDelayMs"], out var ms) ? ms : 1000;
        _retryBaseDelay = TimeSpan.FromMilliseconds(Math.Max(0, retryBaseMs));
    }

    public async Task<ImageEmbeddingResult> EmbedImageAsync(Stream image, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Hugging Face API key is not configured.");

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
            using var request = new HttpRequestMessage(HttpMethod.Post, _model);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            // Block until the serverless model is warm instead of returning a 503 cold-start.
            request.Headers.Add("x-wait-for-model", "true");
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
                _logger.LogError(ex, "Hugging Face feature-extraction request timed out.");
                throw new ExternalServiceException("The Hugging Face request timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network failure while calling the Hugging Face Inference API.");
                throw new ExternalServiceException("Failed to reach the Hugging Face Inference API.");
            }

            using (response)
            {
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests
                    or HttpStatusCode.ServiceUnavailable;
                if (transient && attempt < MaxRetries)
                {
                    var delay = ComputeBackoff(response, attempt, _retryBaseDelay);
                    _logger.LogWarning(
                        "Hugging Face returned {StatusCode}. Backing off {DelayMs}ms before retry {Attempt}/{MaxRetries}.",
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
            "Hugging Face Inference API returned {StatusCode}: {Body}",
            (int)response.StatusCode,
            body);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ExternalServiceException("The Hugging Face API rejected the request. Verify the configured token."),
            HttpStatusCode.NotFound =>
                new ExternalServiceException("The configured Hugging Face model was not found. Verify HuggingFace:Model."),
            HttpStatusCode.BadRequest =>
                new ExternalServiceException("The Hugging Face API rejected the image (unsupported format or too large)."),
            HttpStatusCode.TooManyRequests =>
                new ExternalServiceException("The Hugging Face rate limit was exceeded. Please try again later."),
            _ =>
                new ExternalServiceException("The Hugging Face Inference API is currently unavailable.")
        };
    }

    /// <summary>
    /// The feature-extraction pipeline returns either a flat vector
    /// (<c>[0.1, 0.2, ...]</c>) or a single-element batch (<c>[[0.1, 0.2, ...]]</c>).
    /// Both are flattened to the image's vector; the configured model name is used
    /// as the version so indexed and query embeddings stay comparable.
    /// </summary>
    private ImageEmbeddingResult ExtractEmbedding(string json)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the Hugging Face feature-extraction response.");
            throw new ExternalServiceException("The Hugging Face API returned an unreadable response.");
        }

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new ExternalServiceException("The Hugging Face API returned an empty embedding.");

        // Unwrap a single batch dimension: [[...]] -> [...].
        var vectorElement = root[0].ValueKind == JsonValueKind.Array ? root[0] : root;

        var vector = new float[vectorElement.GetArrayLength()];
        var i = 0;
        foreach (var number in vectorElement.EnumerateArray())
        {
            if (number.ValueKind != JsonValueKind.Number)
                throw new ExternalServiceException("The Hugging Face API returned a malformed embedding.");
            vector[i++] = number.GetSingle();
        }

        if (vector.Length == 0)
            throw new ExternalServiceException("The Hugging Face API returned an empty embedding.");

        return new ImageEmbeddingResult(vector, _model);
    }
}
