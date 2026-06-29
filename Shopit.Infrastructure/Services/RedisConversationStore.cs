using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Chat;
using StackExchange.Redis;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Stores chat conversation history in Redis, scoped per user (SCRUM-109).
///
/// Keys are namespaced as "chat:conversation:{userId}:{conversationId}" so
/// ownership is enforced structurally rather than by an explicit check: even
/// if a caller supplies a conversationId that genuinely belongs to a
/// different user, the read is keyed under the CALLER's own userId and will
/// simply miss, never touching the other user's actual data.
///
/// Redis failures (connection issues, etc.) are caught and treated as a miss
/// on read / a no-op on write, matching the existing CacheService's
/// graceful-degradation behavior, so a Redis outage degrades chat to
/// single-turn rather than taking the endpoint down entirely.
/// </summary>
public class RedisConversationStore : IConversationStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisConversationStore> _logger;
    private readonly TimeSpan _ttl;

    public RedisConversationStore(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<RedisConversationStore> logger)
    {
        _redis = redis;
        _logger = logger;

        var ttlHours = configuration.GetValue<double?>("Chat:ConversationHistoryTtlHours") ?? 24;
        _ttl = TimeSpan.FromHours(ttlHours);
    }

    private static string BuildKey(string conversationId, int userId) =>
        $"chat:conversation:{userId}:{conversationId}";

    public async Task<JsonArray?> GetAsync(string conversationId, int userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(BuildKey(conversationId, userId));

            if (value.IsNullOrEmpty)
            {
                _logger.LogInformation("No stored history for conversation {ConversationId}.", conversationId);
                return null;
            }

            return JsonNode.Parse(value.ToString()) as JsonArray;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read conversation history for {ConversationId} - treating as no history.", conversationId);
            return null;
        }
    }

    public async Task SaveAsync(string conversationId, int userId, JsonArray history, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(BuildKey(conversationId, userId), history.ToJsonString(), _ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save conversation history for {ConversationId} - continuing without persistence.", conversationId);
        }
    }
}