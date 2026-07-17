using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.DTOs.Rag;
using Shopit.Application.Rag;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Answers questions about Shopit's features with retrieval-augmented generation
/// (SCRUM-166): embed the question, retrieve the most similar doc chunks, and —
/// only if a chunk clears the relevance threshold — ask Gemini to answer using
/// those chunks as the sole context, returning citations. If nothing clears the
/// threshold, it returns a fallback stating the answer isn't in the docs without
/// calling Gemini at all, so the model can never guess.
/// </summary>
public class FeatureQaService : IFeatureQaService
{
    public const string HttpClientName = GeminiService.HttpClientName;

    internal const string FallbackAnswer =
        "I don't have that information in the Shopit documentation.";

    private const string SystemInstruction =
        "You are Shopit's friendly shopping assistant, helping everyday customers. Answer the " +
        "user's question using ONLY the provided context, which is drawn from Shopit's feature " +
        "documentation. If the context does not contain the answer, gently say you don't have " +
        "that information. Never use outside knowledge and never invent details. " +
        "Write the way a helpful person would talk: warm, natural, and easy to follow. Speak to " +
        "the customer as \"you\". Keep it short and to the point. Do NOT mention any technical " +
        "details — no API routes or endpoints, no field or parameter names, no status codes, no " +
        "code, and no developer jargon. If the context includes technical wording, translate it " +
        "into plain, everyday language the customer will understand.";

    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FeatureQaService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _topK;
    private readonly double _relevanceThreshold;

    public FeatureQaService(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<FeatureQaService> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Gemini:Model"])
            ? "gemini-2.5-flash"
            : configuration["Gemini:Model"]!;
        _topK = configuration.GetValue<int?>("FeatureQa:TopK") ?? 5;
        _relevanceThreshold = configuration.GetValue<double?>("FeatureQa:RelevanceThreshold") ?? 0.6;
    }

    public async Task<FeatureAnswerResponse> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        if (string.IsNullOrWhiteSpace(question))
            throw new ValidationException("Question must not be empty.");

        var queryEmbedding = await _embeddingService.EmbedAsync(question, cancellationToken);
        var hits = await _vectorStore.SearchAsync(queryEmbedding, _topK, cancellationToken);

        var relevant = hits.Where(h => h.Score >= _relevanceThreshold).ToList();
        if (relevant.Count == 0)
        {
            var best = hits.Count > 0 ? hits[0].Score : 0d;
            _logger.LogInformation(
                "Feature Q&A found no chunk above threshold {Threshold} (best score {BestScore}); returning fallback.",
                _relevanceThreshold, best);
            return new FeatureAnswerResponse(FallbackAnswer, Array.Empty<Citation>(), Answered: false);
        }

        var answer = await GenerateAnswerAsync(question, relevant, cancellationToken);

        var citations = relevant
            .Select(h => new Citation(h.Chunk.FeatureName, h.Chunk.Section, h.Chunk.Audience, h.Chunk.SourceFile))
            .DistinctBy(c => c.SourceFile + "#" + c.Section)
            .ToList();

        return new FeatureAnswerResponse(answer, citations, Answered: true);
    }

    private async Task<string> GenerateAnswerAsync(
        string question,
        IReadOnlyList<ScoredChunk> chunks,
        CancellationToken cancellationToken)
    {
        var context = new StringBuilder();
        foreach (var hit in chunks)
        {
            context.AppendLine(
                $"[Source: {hit.Chunk.FeatureName} — {hit.Chunk.Section} ({hit.Chunk.SourceFile})]");
            context.AppendLine(hit.Chunk.Content);
            context.AppendLine();
        }

        var userText = $"Context:\n{context}\nQuestion: {question}";

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{_model}:generateContent");
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = new StringContent(
            BuildRequestBody(userText).ToJsonString(),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
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
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Gemini API returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                throw new ExternalServiceException("The Gemini API is currently unavailable.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = JsonNode.Parse(body)?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(text))
                throw new ExternalServiceException("The Gemini API returned an empty response.");

            return text.Trim();
        }
    }

    private static JsonObject BuildRequestBody(string userText) => new()
    {
        ["systemInstruction"] = new JsonObject
        {
            ["parts"] = new JsonArray { new JsonObject { ["text"] = SystemInstruction } }
        },
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = userText } }
            }
        }
    };
}
