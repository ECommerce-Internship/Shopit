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
/// Calls the Google Gemini Generate Content endpoint to classify a review comment as
/// genuine or as one of several fraud/abuse categories. Shares GeminiService's HTTP
/// client (same base address) and follows the same request/response handling shape.
/// </summary>
public class ReviewModerationService : IReviewModerationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] ValidCategories =
        { "genuine", "spam", "fake_promotional", "toxic", "incoherent", "off_topic" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ReviewModerationService> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public ReviewModerationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ReviewModerationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Gemini:Model"])
            ? "gemini-2.5-flash"
            : configuration["Gemini:Model"]!;
    }

    public async Task<ReviewModerationVerdict> ModerateReviewAsync(
        string comment,
        int rating,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        var prompt = BuildPrompt(comment, rating);
        var client = _httpClientFactory.CreateClient(GeminiService.HttpClientName);

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
            _logger.LogError(ex, "Gemini API request timed out during review moderation.");
            throw new ExternalServiceException("The Gemini API request timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network failure while calling the Gemini API for review moderation.");
            throw new ExternalServiceException("Failed to reach the Gemini API.");
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);

            var envelope = await response.Content.ReadAsStringAsync(cancellationToken);
            var generatedText = ExtractGeneratedText(envelope);
            var verdict = DeserializeVerdict(generatedText);
            Validate(verdict);
            return verdict;
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError(
            "Gemini API returned {StatusCode} during review moderation: {Body}",
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
            _logger.LogError(ex, "Failed to parse the Gemini API response envelope during review moderation.");
            throw new ExternalServiceException("The Gemini API returned an unreadable response.");
        }

        var text = envelope?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(text))
            throw new ExternalServiceException("The Gemini API returned an empty response.");

        return text;
    }

    private ReviewModerationVerdict DeserializeVerdict(string generatedText)
    {
        var cleaned = StripCodeFences(generatedText);
        try
        {
            var verdict = JsonSerializer.Deserialize<ReviewModerationVerdict>(cleaned, SerializerOptions);
            if (verdict is null)
                throw new ExternalServiceException("The Gemini API returned no moderation verdict.");
            return verdict;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The Gemini API returned invalid JSON during review moderation: {Json}", cleaned);
            throw new ExternalServiceException("The Gemini API returned invalid JSON.");
        }
    }

    private static void Validate(ReviewModerationVerdict verdict)
    {
        if (!ValidCategories.Contains(verdict.Category))
            verdict.Category = verdict.IsSuspicious ? "spam" : "genuine";
    }

    private static string BuildPrompt(string comment, int rating)
    {
        return $$"""
            You are a content moderation classifier for an e-commerce marketplace's product reviews.
            Classify the following review comment.

            Star rating given: {{rating}}/5
            Review comment: "{{comment}}"

            Categories: genuine, spam, fake_promotional, toxic, incoherent, off_topic.

            Produce:
            - isSuspicious: true if the comment is anything other than a genuine product review.
            - category: exactly one of the categories above.
            - confidence: a number between 0 and 1.
            - reason: a short (under 100 characters) explanation of the classification.

            Rules:
            - Return only valid JSON that matches the requested schema.
            - Do not use markdown formatting.
            - Do not include any explanatory text outside the JSON.
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
                        isSuspicious = new { type = "BOOLEAN" },
                        category = new { type = "STRING" },
                        confidence = new { type = "NUMBER" },
                        reason = new { type = "STRING" }
                    },
                    required = new[] { "isSuspicious", "category", "confidence", "reason" }
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
