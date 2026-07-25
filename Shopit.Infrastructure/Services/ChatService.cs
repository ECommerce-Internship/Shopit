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
/// Shopit MCP server. Each call connects to the MCP server fresh over HTTP
/// (SCRUM-153 - previously stdio, spawning the server as a child process per
/// request), lists its tools, filters them by the caller's role, and runs a
/// function-calling loop with Gemini (capped at <see cref="MaxIterations"/>
/// turns) until Gemini returns a plain text reply.
///
/// Conversation persistence (SCRUM-109): prior turns are loaded from
/// <see cref="IConversationStore"/> at the start of the request (scoped to
/// the calling user), the new user message and the final assistant reply are
/// appended, the combined history is trimmed to <see cref="_maxHistoryEntries"/>
/// entries, and the result is saved back before returning. This is only done
/// on the successful path — if Gemini or an MCP tool call fails partway
/// through, nothing is persisted for that turn, so a failed attempt never
/// pollutes the stored history with a confusing partial state.
///
/// Security model (see SCRUM-106, extended in SCRUM-108):
///   1. Tool filtering — <see cref="GetToolsForRole"/> strips admin-only tools
///      out of functionDeclarations entirely for non-admin roles, so the model
///      cannot call what it cannot see.
///   2. Identity injection — for tools that operate on the caller's own data
///      (<see cref="IdentityInjectedTools"/>), the userId argument is always
///      overwritten with the value from the caller's JWT claims before the
///      tool executes, regardless of what the model supplied.
///   3. Hidden parameters — for self-scoped customer tools (add_to_cart,
///      view_cart, get_my_orders), the underlying MCP tool method still takes
///      a "userId" C# parameter (the MCP SDK only excludes parameters from the
///      schema for special bound types like CancellationToken, not plain int),
///      but that parameter is stripped out of the JSON schema sent to Gemini
///      via <see cref="RemoveHiddenParameters"/> so the model never sees or
///      sets it. The real value is still supplied via identity injection at
///      call time. get_customer_orders is intentionally NOT in this list —
///      its userId stays visible/admin-settable by design.
/// </summary>
public class ChatService : IChatService
{
    public const string HttpClientName = "GeminiClient";

    private const int MaxIterations = 5;

    /// <summary>
    /// Tools that operate on the caller's own data. Any "userId" argument the
    /// model supplies for these tools is discarded and replaced with the
    /// caller's JWT-derived userId before the tool is invoked.
    /// </summary>
    private static readonly HashSet<string> IdentityInjectedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_customer_orders",
        "add_to_cart",
        "view_cart",
        "get_my_orders",
    };

    /// <summary>
    /// Tools a non-admin caller is allowed to see and invoke. Anything not
    /// listed here (e.g. get_dashboard_summary, get_low_stock_products,
    /// get_order) is stripped from functionDeclarations entirely for
    /// non-admin roles.
    /// </summary>
    private static readonly HashSet<string> CustomerAllowedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "search_products",
        "add_to_cart",
        "view_cart",
        "get_my_orders",
        // SCRUM-166: feature Q&A is available to every authenticated user (the docs
        // cover both customer- and seller-facing features). It takes no userId, so it
        // is intentionally absent from IdentityInjectedTools / HiddenParametersByTool.
        "answer_feature_question",
    };

    /// <summary>
    /// Tools where an Admin caller's own "userId" argument is honored as-is,
    /// rather than being overwritten by identity injection. Currently just
    /// get_customer_orders, so an Admin can look up any customer's orders by
    /// ID. Non-admin callers are unaffected by this set — identity injection
    /// still always overrides their "userId" for any tool in
    /// IdentityInjectedTools, so a non-admin can never spoof another user's ID
    /// here either.
    /// </summary>
    private static readonly HashSet<string> AdminOverridableTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_customer_orders",
    };

    /// <summary>
    /// Per SCRUM-108: tools whose schema sent to Gemini must have specific
    /// parameter names stripped out entirely, even though the underlying MCP
    /// tool method still accepts them (and identity injection still supplies
    /// them at call time). Used for self-scoped tools where "userId" must
    /// never be visible to or settable by the model.
    /// get_customer_orders is deliberately excluded — its userId stays
    /// visible/admin-settable by design.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> HiddenParametersByTool =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["add_to_cart"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "userId" },
            ["view_cart"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "userId" },
            ["get_my_orders"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "userId" },
        };

    private const string AdminRole = "Admin";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConversationStore _conversationStore;
    private readonly ILogger<ChatService> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _mcpBaseUrl;
    private readonly int _maxHistoryEntries;

    public ChatService(
        IHttpClientFactory httpClientFactory,
        IConversationStore conversationStore,
        IConfiguration configuration,
        ILogger<ChatService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _conversationStore = conversationStore;
        _logger = logger;
        _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(configuration["Gemini:Model"])
            ? "gemini-2.5-flash"
            : configuration["Gemini:Model"]!;
        // SCRUM-153: the MCP server's base URL, externalized so it can point
        // at a local dev instance or the shopit-mcp Docker service without
        // any code change — see Mcp:BaseUrl in appsettings.json.
        _mcpBaseUrl = configuration["Mcp:BaseUrl"] ?? string.Empty;
        _maxHistoryEntries = configuration.GetValue<int?>("Chat:MaxHistoryEntries") ?? 20;
    }

    public async Task<ChatResponse> SendMessageAsync(
        string message,
        string? conversationId,
        int userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new ExternalServiceException("Gemini API key is not configured.");

        if (string.IsNullOrWhiteSpace(_mcpBaseUrl))
            throw new ExternalServiceException("The MCP server URL is not configured.");

        var resolvedConversationId = string.IsNullOrWhiteSpace(conversationId)
            ? Guid.NewGuid().ToString()
            : conversationId;

        // SCRUM-109: load prior history for this conversation, scoped to the
        // calling user. Returns null for a brand-new conversation, or if the
        // supplied id doesn't belong to this user — see RedisConversationStore's
        // key-namespacing for why a cross-user attempt is safe rather than an
        // error: it simply behaves like a fresh conversation under this id.
        var history = string.IsNullOrWhiteSpace(conversationId)
            ? null
            : await _conversationStore.GetAsync(conversationId, userId, cancellationToken);

        var contents = history?.DeepClone() as JsonArray ?? new JsonArray();
        contents.Add(BuildUserTextContent(message));

        // SCRUM-153: connect to the MCP server over HTTP rather than spawning
        // it as a child process via stdio. The server now runs as its own
        // independent process/container, reachable at _mcpBaseUrl.
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_mcpBaseUrl),
        });

        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        var allTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        var allowedTools = GetToolsForRole(role, allTools);
        var functionDeclarations = BuildFunctionDeclarations(allowedTools);

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var responseJson = await CallGeminiAsync(contents, functionDeclarations, cancellationToken);
            var candidatePart = ExtractFirstPart(responseJson);

            var functionCall = candidatePart?["functionCall"];
            var thoughtSignature = candidatePart?["thoughtSignature"]?.GetValue<string>();
            if (functionCall is null)
            {
                var text = candidatePart?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                    throw new ExternalServiceException("The Gemini API returned an empty response.");

                // SCRUM-109: append the final assistant reply (previously
                // discarded once the request ended) so the next turn's loaded
                // history includes it, then trim to bound Gemini's context
                // window before persisting.
                contents.Add(BuildModelTextContent(text));
                var trimmedHistory = TrimHistory(contents, _maxHistoryEntries);
                await _conversationStore.SaveAsync(resolvedConversationId, userId, trimmedHistory, cancellationToken);

                return new ChatResponse(text, resolvedConversationId);
            }

            var functionName = functionCall["name"]!.GetValue<string>();
            var argsNode = functionCall["args"] as JsonObject;

            // Defence in depth: even though tool filtering already hides
            // disallowed tools from the model, never execute a tool that
            // didn't make it through the role filter, in case the model
            // somehow still attempts to call one.
            if (!allowedTools.Any(t => string.Equals(t.Name, functionName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(
                    "Blocked call to tool {ToolName} not permitted for role {Role}.",
                    functionName, role);

                contents.Add(BuildModelFunctionCallContent(functionName, argsNode, thoughtSignature));
                contents.Add(BuildFunctionResponseContent(functionName, "Tool call denied: not permitted for this user's role."));
                continue;
            }

            // Identity injection: the model never decides whose data it reads.
            // This is delegated to a pure, separately-testable method so the
            // override behavior can be unit tested without needing a real
            // MCP connection or HTTP mocking.
            var argsObject = ApplyIdentityInjection(functionName, argsNode, userId, role);

            var arguments = ConvertArgsToDictionary(argsObject);

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
            contents.Add(BuildModelFunctionCallContent(functionName, argsObject, thoughtSignature));
            contents.Add(BuildFunctionResponseContent(functionName, toolResultText));
        }

        throw new ExternalServiceException("The assistant did not produce a final reply within the allowed number of tool-call iterations.");
    }

    /// <summary>
    /// Filters the full MCP tool list down to what a caller with the given
    /// role is permitted to see and invoke. Admins see every tool; any other
    /// role sees only <see cref="CustomerAllowedTools"/>. Tools not returned
    /// here are completely absent from the functionDeclarations sent to
    /// Gemini, so the model cannot call what it cannot see.
    /// </summary>
    public static IReadOnlyList<McpClientTool> GetToolsForRole(string role, IEnumerable<McpClientTool> allTools)
    {
        var allToolsList = allTools.ToList();
        var allowedNames = GetAllowedToolNames(role, allToolsList.Select(t => t.Name));
        return allToolsList.Where(t => allowedNames.Contains(t.Name)).ToList();
    }

    /// <summary>
    /// Pure, name-based version of the role filter used by <see cref="GetToolsForRole"/>.
    /// Kept separate (and public) so the filtering rule itself can be unit tested
    /// without needing to construct real McpClientTool instances.
    /// </summary>
    public static HashSet<string> GetAllowedToolNames(string role, IEnumerable<string> allToolNames)
    {
        var names = allToolNames.ToList();

        if (string.Equals(role, AdminRole, StringComparison.OrdinalIgnoreCase))
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(
            names.Where(CustomerAllowedTools.Contains),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the argument set that will actually be sent to an MCP tool call,
    /// after overwriting any identity-related argument the model supplied.
    ///
    /// For tools in <see cref="IdentityInjectedTools"/> (tools that operate on
    /// the caller's own data, e.g. get_customer_orders, add_to_cart, view_cart,
    /// get_my_orders), the "userId" argument is always replaced with
    /// <paramref name="callerUserId"/> — taken from the caller's JWT claims —
    /// regardless of what value the model supplied (or, for the three
    /// SCRUM-108 tools, regardless of the fact the model never even sees
    /// "userId" as an option, since it's hidden from the schema). This is the
    /// backstop defence from SCRUM-106: even if tool filtering somehow let an
    /// identity-sensitive tool through, the model can never read or modify
    /// another user's data because it cannot control whose userId is actually
    /// used.
    ///
    /// Pure and side-effect free so this exact security behavior can be unit
    /// tested without a real MCP connection.
    ///
    /// Exception: for tools in <see cref="AdminOverridableTools"/> (currently
    /// just get_customer_orders), an Admin caller's own "userId" argument is
    /// honored as-is instead of being overwritten, so an Admin can look up any
    /// customer's orders by ID. Non-admin callers still always have "userId"
    /// overridden for these tools, so a non-admin can never spoof another
    /// user's ID this way.
    /// </summary>
    public static JsonObject ApplyIdentityInjection(string toolName, JsonObject? modelSuppliedArgs, int callerUserId, string callerRole)
    {
        var args = modelSuppliedArgs?.DeepClone() as JsonObject ?? new JsonObject();

        var isAdminOverride = AdminOverridableTools.Contains(toolName)
            && string.Equals(callerRole, AdminRole, StringComparison.OrdinalIgnoreCase);

        if (IdentityInjectedTools.Contains(toolName) && !isAdminOverride)
            args["userId"] = callerUserId;

        return args;
    }

    /// <summary>
    /// Trims a contents history array down to at most <paramref name="maxEntries"/>
    /// of its most recent entries (SCRUM-109), to keep Gemini's context window
    /// bounded as a conversation grows over many turns. Each retained entry is
    /// deep-cloned, since a JsonNode can only have a single parent and the
    /// source array may still be in use by the caller.
    ///
    /// Note: "entries" here are the raw items in the Gemini "contents" array
    /// (one per user message, model function call, function response, or
    /// model text reply) rather than a count of whole conversational turns,
    /// since a single turn can expand into several entries when tool calls are
    /// involved. A maxEntries of 20 therefore covers somewhat fewer than 20
    /// full back-and-forth exchanges, which is an intentionally conservative
    /// approximation in favor of never silently dropping a recent turn.
    /// </summary>
    private static JsonArray TrimHistory(JsonArray contents, int maxEntries)
    {
        if (contents.Count <= maxEntries)
            return contents;

        var trimmed = new JsonArray();
        var startIndex = contents.Count - maxEntries;
        for (var i = startIndex; i < contents.Count; i++)
            trimmed.Add(contents[i]?.DeepClone());

        return trimmed;
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

    private static JsonObject BuildModelTextContent(string text) => new()
    {
        ["role"] = "model",
        ["parts"] = new JsonArray { new JsonObject { ["text"] = text } }
    };

    private static JsonObject BuildModelFunctionCallContent(string name, JsonObject? args, string? thoughtSignature = null)
    {
        var part = new JsonObject
        {
            ["functionCall"] = new JsonObject
            {
                ["name"] = name,
                ["args"] = args?.DeepClone() ?? new JsonObject()
            }
        };
        if (!string.IsNullOrEmpty(thoughtSignature))
        {
            part["thoughtSignature"] = thoughtSignature;
        }
        return new JsonObject
        {
            ["role"] = "model",
            ["parts"] = new JsonArray { part }
        };
    }

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

    private static JsonArray BuildFunctionDeclarations(IEnumerable<McpClientTool> tools)
    {
        var declarations = new JsonArray();
        foreach (var tool in tools)
        {
            var schemaNode = JsonNode.Parse(tool.JsonSchema.GetRawText()) as JsonObject ?? new JsonObject();
            var sanitized = SanitizeSchemaForGemini(schemaNode) as JsonObject ?? new JsonObject();

            // SCRUM-108: strip any parameters configured in HiddenParametersByTool
            // (currently just "userId" on add_to_cart / view_cart / get_my_orders)
            // out of the schema Gemini sees, after general sanitization. The real
            // value is still supplied later via ApplyIdentityInjection.
            if (HiddenParametersByTool.TryGetValue(tool.Name, out var hiddenParams))
                RemoveHiddenParameters(sanitized, hiddenParams);

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
    /// Removes the given parameter names from a (already-sanitized) Gemini
    /// function-parameters schema object, in place: deletes each name from
    /// "properties" and from the "required" array if present. Per SCRUM-108,
    /// this is how "userId" is kept off the schema sent to Gemini for
    /// self-scoped tools (add_to_cart, view_cart, get_my_orders) even though
    /// the underlying MCP tool method still declares the parameter.
    /// </summary>
    private static void RemoveHiddenParameters(JsonObject schema, HashSet<string> parameterNames)
    {
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var name in parameterNames)
                properties.Remove(name);
        }

        if (schema["required"] is JsonArray required)
        {
            for (var i = required.Count - 1; i >= 0; i--)
            {
                var requiredName = required[i]?.GetValue<string>();
                if (requiredName is not null && parameterNames.Contains(requiredName))
                    required.RemoveAt(i);
            }
        }
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