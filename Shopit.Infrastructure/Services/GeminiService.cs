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
/// Calls the Google Gemini Generate Content endpoint to produce structured product
/// marketing content. Handles upstream failures and validates the generated response.
/// </summary>
public class GeminiService : IGeminiService
{
    public const string HttpClientName = "GeminiClient";

    private const int RequiredFeatureCount = 5;
    private const int MaxSeoTitleLength = 60;
    private const int MaxMetaDescriptionLength = 155;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GeminiService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Gemini:Model"])
            ? "gemini-2.5-flash"
            : configuration["Gemini:Model"]!;
    }

    public async Task<ProductContentResponse> GenerateProductContentAsync(
        string productName,
        string category,
        string specs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        var prompt = BuildPrompt(productName, category, specs);
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{_model}:generateContent");
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(BuildRequestBody(prompt)),
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
            // HttpClient.Timeout elapsed (surfaces as a cancellation that the caller did not request).
            _logger.LogError(ex, "Gemini API request timed out.");
            throw new ExternalServiceException("The Gemini API request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network failure while calling the Gemini API.");
            throw new ExternalServiceException("Failed to reach the Gemini API.");
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);

            var envelope = await response.Content.ReadAsStringAsync(cancellationToken);
            var generatedText = ExtractGeneratedText(envelope);
            var content = DeserializeContent(generatedText);
            Validate(content);
            return content;
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Gemini API returned {StatusCode}: {Body}",
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

    private string ExtractGeneratedText(string envelopeJson)
    {
        GeminiResponse? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<GeminiResponse>(envelopeJson, SerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse the Gemini API response envelope.");
            throw new ExternalServiceException("The Gemini API returned an unreadable response.");
        }

        var text = envelope?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new ExternalServiceException("The Gemini API returned an empty response.");

        return text;
    }

    private ProductContentResponse DeserializeContent(string generatedText)
    {
        var cleaned = StripCodeFences(generatedText);
        try
        {
            var content = JsonSerializer.Deserialize<ProductContentResponse>(cleaned, SerializerOptions);
            if (content is null)
                throw new ExternalServiceException("The Gemini API returned no content.");
            return content;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The Gemini API returned invalid JSON: {Json}", cleaned);
            throw new ExternalServiceException("The Gemini API returned invalid JSON.");
        }
    }

    private static void Validate(ProductContentResponse content)
    {
        if (string.IsNullOrWhiteSpace(content.Description))
            throw new ExternalServiceException("Generated content is missing a description.");

        if (content.Features is null || content.Features.Count != RequiredFeatureCount)
            throw new ExternalServiceException($"Generated content must contain exactly {RequiredFeatureCount} features.");

        if (content.Features.Any(string.IsNullOrWhiteSpace))
            throw new ExternalServiceException("Generated content contains an empty feature.");

        if (string.IsNullOrWhiteSpace(content.SeoTitle) || content.SeoTitle.Length > MaxSeoTitleLength)
            throw new ExternalServiceException($"Generated SEO title must be between 1 and {MaxSeoTitleLength} characters.");

        if (string.IsNullOrWhiteSpace(content.MetaDescription) || content.MetaDescription.Length > MaxMetaDescriptionLength)
            throw new ExternalServiceException($"Generated meta description must be between 1 and {MaxMetaDescriptionLength} characters.");
    }

    private static string BuildPrompt(string productName, string category, string specs)
    {
        return $$"""
            You are a professional e-commerce copywriter.
            Generate marketing content for the following product.

            Product name: {{productName}}
            Category: {{category}}
            Specifications: {{specs}}

            Produce:
            - description: 2-3 engaging paragraphs.
            - features: exactly 5 concise product features.
            - seoTitle: an SEO title under 60 characters.
            - metaDescription: a meta description under 155 characters.

            Rules:
            - Return only valid JSON that matches the requested schema.
            - Do not use markdown formatting.
            - Do not include any explanatory text.
            - Do not invent specifications that were not provided.
            """;
    }

    private static object BuildRequestBody(string prompt)
    {
        return new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        description = new { type = "STRING" },
                        features = new { type = "ARRAY", items = new { type = "STRING" } },
                        seoTitle = new { type = "STRING" },
                        metaDescription = new { type = "STRING" }
                    },
                    required = new[] { "description", "features", "seoTitle", "metaDescription" }
                }
            }
        };
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```"))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline >= 0)
            trimmed = trimmed[(firstNewline + 1)..];

        if (trimmed.EndsWith("```"))
            trimmed = trimmed[..^3];

        return trimmed.Trim();
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; set; }
    }

    private sealed class Candidate
    {
        [JsonPropertyName("content")]
        public Content? Content { get; set; }
    }

    private sealed class Content
    {
        [JsonPropertyName("parts")]
        public List<Part>? Parts { get; set; }
    }

    private sealed class Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
