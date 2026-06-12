using System.Text.Json;
using Serilog;
using Shopit.Application.Interfaces;
using StackExchange.Redis;

namespace Shopit.Infrastructure.Services;

public class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;

    public CacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);

        if (value.IsNullOrEmpty)
        {
            Log.Information("Cache MISS for key {CacheKey}", key);
            return default;
        }

        Log.Information("Cache HIT for key {CacheKey}", key);
        return JsonSerializer.Deserialize<T>(value.ToString());
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        var db = _redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, serialized, expiry);
        Log.Information("Cache SET for key {CacheKey} with expiry {Expiry}", key, expiry);
    }

    public async Task RemoveAsync(string key)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(key);
        Log.Information("Cache REMOVE for key {CacheKey}", key);
    }

    public async Task RemoveByPatternAsync(string pattern)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: pattern).ToArray();

        if (keys.Length == 0)
        {
            Log.Information("Cache pattern REMOVE: no keys matched {Pattern}", pattern);
            return;
        }

        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(keys);
        Log.Information("Cache pattern REMOVE: deleted {Count} keys matching {Pattern}", keys.Length, pattern);
    }
}