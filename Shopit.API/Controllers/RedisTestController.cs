using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Shopit.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RedisTestController : ControllerBase
{
    private readonly IDatabase _redis;

    public RedisTestController(IConnectionMultiplexer redis)
    {
        _redis = redis.GetDatabase();
    }

    [HttpGet]
    public async Task<IActionResult> Test()
    {
        await _redis.StringSetAsync("test-key", "Hello from Redis!");
        var value = await _redis.StringGetAsync("test-key");
        return Ok(new { message = value.ToString() });
    }
}