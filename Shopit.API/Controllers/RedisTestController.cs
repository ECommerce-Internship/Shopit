using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using Serilog;

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
        // Structured log 1 — cache hit simulation
        Log.Information("Cache HIT for product {ProductId}", 42);

        await _redis.StringSetAsync("test-key", "Hello from Redis!");
        var value = await _redis.StringGetAsync("test-key");

        // Structured log 2 — successful retrieval
        Log.Information("Redis value retrieved successfully: {Value}", value.ToString());

        return Ok(new { message = value.ToString() });
    }

    [HttpGet("low-stock")]
    public IActionResult SimulateLowStock()
    {
        // Structured log 3 — low stock warning
        Log.Warning("Low stock triggered: {ProductName} qty={Qty}", "Laptop", 2);
        return Ok(new { warning = "Low stock simulated" });
    }

    [HttpGet("not-found")]
    public IActionResult SimulateNotFound()
    {
        // Structured log 4 — 404 simulation
        Log.Warning("Product not found for {ProductId}", 999);
        return NotFound(new { error = "Product 999 not found" });
    }

    [HttpGet("error")]
    public IActionResult SimulateError()
    {
        // Structured log 5 — error simulation
        Log.Error("Unexpected error processing order {OrderId}", 101);
        return StatusCode(500, new { error = "Simulated error" });
    }
}