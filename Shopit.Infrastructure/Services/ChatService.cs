using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shopit.Application.Chat;
using Shopit.Domain.Exceptions;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Bridges a chat conversation between Gemini's function-calling API and the
/// Shopit MCP server. Each call connects to the MCP server fresh, lists its
/// tools, and runs a function-calling loop with Gemini (capped at
/// <see cref="MaxIterations"/> turns) until Gemini returns a plain text reply.
/// Conversation state is kept in memory for the duration of this single request only.
/// </summary>
public class ChatService : IChatService
{
    public const string HttpClientName = "GeminiClient";

    private const int MaxIterations = 5;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _mcpExePath;
    private readonly string _mcpWorkingDirectory;

    public ChatService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Gemini:Model"])
            ? "gemini-2.5-flash"
            : configuration["Gemini:Model"]!;
        _mcpExePath = configuration["Gemini:McpExePath"] ?? string.Empty;
        _mcpWorkingDirectory = configuration["Gemini:McpWorkingDirectory"] ?? string.Empty;
    }

    public async Task<ChatResponse> SendMessageAsync(
        string message,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        if (string.IsNullOrWhiteSpace(_mcpExePath) || string.IsNullOrWhiteSpace(_mcpWorkingDirectory))
            throw new ExternalServiceException("The MCP server path is not configured.");

        var resolvedConversationId = string.IsNullOrWhiteSpace(conversationId)
            ? Guid.NewGuid().ToString()
            : conversationId;

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Shopit.MCP",
            Command = _mcpExePath,
            WorkingDirectory = _mcpWorkingDirectory,
        });

        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        var functionDeclarations = BuildFunctionDeclarations(tools);

        var contents = new JsonArray
        {
            BuildUserTextContent(message)
        };

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var responseJson = await CallGeminiAsync(contents, functionDeclarations, cancellationToken);
            var candidatePart = ExtractFirstPart(responseJson);

            var functionCall = candidatePart?["functionCall"];
            if (functionCall is null)
            {
                var text = candidatePart?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                    throw new ExternalServiceException("The Gemini API returned an empty response.");

                return new ChatResponse(text, resolvedConversationId);
            }

            var functionName = functionCall["name"]!.GetValue<string>();
            var argsNode = functionCall["args"] as JsonObject;
            var arguments = ConvertArgsToDictionary(argsNode);

            _logger.LogInformation("Gemini requested tool call: {ToolName}", functionName);

            string toolResultText;
            try
            {
                var toolResult = await mcpClient.CallToolAsync(
                    functionName,
                    arguments,
                    cancellationToken: cancellationToken);
                toolResultText = ExtractToolResultText(toolResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP tool call failed: {ToolName}", functionName);
                toolResultText = $"Tool call failed: {ex.Message}";
            }

            // Append the model's function call turn, then our function response turn.
            contents.Add(BuildModelFunctionCallContent(functionName, argsNode));
            contents.Add(BuildFunctionResponseContent(functionName, toolResultText));
        }

        throw new ExternalServiceException("The assistant did not produce a final reply within the allowed number of tool-call iterations.");
    }

    private async Task<JsonNode> CallGeminiAsync(
        JsonArray contents,
        JsonArray functionDeclarations,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            var requestBody = new JsonObject
            {
                ["contents"] = contents.DeepClone(),
                ["tools"] = new JsonArray
                {
                    new JsonObject { ["functionDeclarations"] = functionDeclarations.DeepClone() }
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"v1beta/models/{_model}:generateContent");
            request.Headers.Add("x-goog-api-key", _apiKey);
            request.Content = new StringContent(requestBody.ToJsonString(), System.Text.Encoding.UTF8, "application/json");

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
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return JsonNode.Parse(body) ?? throw new ExternalServiceException("The Gemini API returned an unreadable response.");
                }

                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // 503 from Gemini typically means transient overload on Google's side
                // rather than a problem with the request — retry a couple of times
                // with a short backoff before giving up.
                if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable && attempt < maxAttempts)
                {
                    _logger.LogWarning(
                        "Gemini API returned 503 (attempt {Attempt}/{MaxAttempts}), retrying: {Body}",
                        attempt, maxAttempts, errorBody);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
                    continue;
                }

                _logger.LogError("Gemini API returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
                throw new ExternalServiceException("The Gemini API is currently unavailable.");
            }
        }

        throw new ExternalServiceException("The Gemini API is currently unavailable.");
    }

    private static JsonObject BuildUserTextContent(string text) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    private static JsonObject BuildModelFunctionCallContent(string name, JsonObject? args) => new()
    {
        ["role"] = "model",
        ["parts"] = new JsonArray
        {
            new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = name,
                    ["args"] = args?.DeepClone() ?? new JsonObject()
                }
            }
        }
    };

    private static JsonObject BuildFunctionResponseContent(string name, string resultText) => new()
    {
        ["role"] = "user",
        ["parts"] = new JsonArray
        {
            new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = name,
                    ["response"] = new JsonObject { ["result"] = resultText }
                }
            }
        }
    };

    private static JsonNode? ExtractFirstPart(JsonNode responseJson)
    {
        return responseJson["candidates"]?[0]?["content"]?["parts"]?[0];
    }

    private static JsonArray BuildFunctionDeclarations(IList<McpClientTool> tools)
    {
        var declarations = new JsonArray();
        foreach (var tool in tools)
        {
            var schemaNode = JsonNode.Parse(tool.JsonSchema.GetRawText()) as JsonObject ?? new JsonObject();
            var sanitized = SanitizeSchemaForGemini(schemaNode) as JsonObject ?? new JsonObject();

            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description ?? string.Empty,
                ["parameters"] = sanitized
            });
        }
        return declarations;
    }

    /// <summary>
    /// Recursively rewrites a standard JSON Schema node into the subset Gemini's
    /// function-calling API accepts: drops keywords Gemini rejects (e.g. $schema,
    /// additionalProperties), collapses nullable "type": ["string", "null"] arrays
    /// into a single value, and uppercases type names (Gemini expects STRING,
    /// OBJECT, ARRAY, etc. rather than the lowercase JSON Schema convention).
    /// </summary>
    private static JsonNode? SanitizeSchemaForGemini(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var (key, value) in obj)
            {
                if (key is "$schema" or "additionalProperties" or "title")
                    continue;

                if (key == "type")
                {
                    var typeName = value switch
                    {
                        JsonArray typeArray => typeArray
                            .Select(t => t?.GetValue<string>())
                            .FirstOrDefault(t => t is not null && t != "null"),
                        JsonValue typeValue => typeValue.GetValue<string>(),
                        _ => null
                    };

                    if (typeName is not null)
                        result["type"] = typeName.ToUpperInvariant();

                    continue;
                }

                result[key] = SanitizeSchemaForGemini(value?.DeepClone());
            }
            return result;
        }

        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            foreach (var item in arr)
                result.Add(SanitizeSchemaForGemini(item?.DeepClone()));
            return result;
        }

        return node;
    }

    private static Dictionary<string, object?> ConvertArgsToDictionary(JsonObject? args)
    {
        var result = new Dictionary<string, object?>();
        if (args is null)
            return result;

        foreach (var (key, value) in args)
        {
            result[key] = value switch
            {
                null => null,
                JsonValue jsonValue when jsonValue.TryGetValue(out string? s) => s,
                JsonValue jsonValue when jsonValue.TryGetValue(out int i) => i,
                JsonValue jsonValue when jsonValue.TryGetValue(out double d) => d,
                JsonValue jsonValue when jsonValue.TryGetValue(out bool b) => b,
                _ => value.ToJsonString()
            };
        }
        return result;
    }

    private static string ExtractToolResultText(CallToolResult toolResult)
    {
        var texts = toolResult.Content
            .OfType<TextContentBlock>()
            .Select(t => t.Text);
        var combined = string.Join("\n", texts);
        return string.IsNullOrWhiteSpace(combined) ? "(tool returned no content)" : combined;
    }
}