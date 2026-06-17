using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shopit.Application.AI;
using Shopit.Domain.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Shopit.Infrastructure.Services;

public class GeminiService : IGeminiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ProductContentResponse> GenerateProductContentAsync(
        string productName,
        string category,
        string specs,
        decimal price,
        CancellationToken cancellationToken = default)
    {
        ValidateInput(productName, category, specs);

        var settings = _configuration
            .GetSection("Gemini")
            .Get<GeminiSettings>() ?? new GeminiSettings();

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new ValidationException("Gemini API key is missing. Add it to appsettings.Development.json or environment variables.");

        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new ValidationException("Gemini model is missing from configuration.");

        var prompt = BuildPrompt(productName, category, specs, price);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new
                        {
                            text = prompt
                        }
                    }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        var client = _httpClientFactory.CreateClient("GeminiClient");

        var endpoint =
            $"v1beta/models/{settings.Model}:generateContent?key={Uri.EscapeDataString(settings.ApiKey)}";

        try
        {
            using var response = await client.PostAsJsonAsync(endpoint, requestBody, cancellationToken);

            if (!response.IsSuccessStatusCode)
                await HandleGeminiErrorAsync(response, cancellationToken);

            var geminiResponse = await response.Content
                .ReadFromJsonAsync<GeminiGenerateContentResponse>(JsonOptions, cancellationToken);

            var jsonText = ExtractJsonText(geminiResponse);

            var generatedContent = JsonSerializer.Deserialize<ProductContentResponse>(jsonText, JsonOptions);

            if (generatedContent is null)
                throw new ValidationException("Gemini returned invalid JSON content.");

            ValidateGeneratedResponse(generatedContent);

            return generatedContent;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Gemini request timed out.");
            throw new ConflictException("Gemini request timed out. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while calling Gemini.");
            throw new ConflictException("Could not connect to Gemini. Please try again later.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Gemini returned invalid JSON.");
            throw new ValidationException("Gemini returned invalid JSON.");
        }
    }

    private static void ValidateInput(string productName, string category, string specs)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new ValidationException("Product name is required.");

        if (string.IsNullOrWhiteSpace(category))
            throw new ValidationException("Category is required.");

        if (string.IsNullOrWhiteSpace(specs))
            throw new ValidationException("Product specifications are required.");
    }

    private static string BuildPrompt(string productName, string category, string specs, decimal price)
    {
    return $$"""
    You are a professional e-commerce copywriter.

    Generate product content using only the product information provided below.

    Product name: {{productName}}
    Category: {{category}}
    Price: ${{price:F2}}
    Product specifications: {{specs}}

    Requirements:
    - Generate a brief product description in 1 to 2 sentences, under 155 characters.
    - Generate exactly five product features.
    - Generate an SEO title under 60 characters.
    - Generate a meta description under 155 characters.
    - Use the price to determine product positioning: under $50 is budget, $50-$300 is mid-range, above $300 is premium. Match the copy tone accordingly.
    - If the category is expressed as a hierarchy (e.g., "Electronics > Phones"), use both levels for context.
    - Do not invent specifications that were not provided.
    - Do not use markdown.
    - Do not include explanatory text.
    - Return only valid JSON.

    Return the JSON in this exact shape:
    {
      "description": "string",
      "features": [
        "string",
        "string",
        "string",
        "string",
        "string"
      ],
      "seoTitle": "string",
      "metaDescription": "string"
    }
    """;
}

    private static string ExtractJsonText(GeminiGenerateContentResponse? response)
    {
        var text = response?
            .Candidates?
            .FirstOrDefault()?
            .Content?
            .Parts?
            .FirstOrDefault()?
            .Text;

        if (string.IsNullOrWhiteSpace(text))
            throw new ValidationException("Gemini returned an empty response.");

        return CleanJsonText(text);
    }

    private static string CleanJsonText(string text)
    {
        var cleaned = text.Trim();

        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[7..].Trim();

        if (cleaned.StartsWith("```"))
            cleaned = cleaned[3..].Trim();

        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3].Trim();

        return cleaned;
    }

    private static void ValidateGeneratedResponse(ProductContentResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Description))
            throw new ValidationException("Generated description is empty.");

        if (response.Description.Length > 155)
            throw new ValidationException("Generated description exceeds 155 characters.");

        if (response.Features is null || response.Features.Count != 5)
            throw new ValidationException("Generated features must contain exactly five items.");

        if (response.Features.Any(string.IsNullOrWhiteSpace))
            throw new ValidationException("Generated features cannot contain empty values.");

        if (string.IsNullOrWhiteSpace(response.SeoTitle))
            throw new ValidationException("Generated SEO title is empty.");

        if (response.SeoTitle.Length > 60)
            throw new ValidationException("Generated SEO title exceeds 60 characters.");

        if (string.IsNullOrWhiteSpace(response.MetaDescription))
            throw new ValidationException("Generated meta description is empty.");

        if (response.MetaDescription.Length > 155)
            throw new ValidationException("Generated meta description exceeds 155 characters.");
    }

    private async Task HandleGeminiErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

        // Log the raw upstream body server-side only; never surface it to the caller.
        _logger.LogError(
            "Gemini API request failed with status code {StatusCode}. Response body: {ErrorBody}",
            (int)response.StatusCode,
            errorBody);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedException("Invalid Gemini API key or access is forbidden.");

        if ((int)response.StatusCode == 429)
            throw new ConflictException("Gemini rate limit reached. Please try again later.");

        if ((int)response.StatusCode == 503)
            throw new ConflictException("Gemini is temporarily unavailable or overloaded. Please try again later.");

        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new ValidationException("Gemini could not process the request. Please revise the product details and try again.");

        throw new ConflictException("Gemini content generation failed. Please try again later.");
    }

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }

    private sealed class GeminiContent
    {
        public List<GeminiPart>? Parts { get; set; }
    }

    private sealed class GeminiPart
    {
        public string? Text { get; set; }
    }
}