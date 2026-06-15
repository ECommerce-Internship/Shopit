using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrderController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() =>
        User.IsInRole("Admin");

    // Customer endpoints
    [HttpPost]
    [Route("api/v{version:apiVersion}/orders")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest request)
    {
        var result = await _orderService.PlaceOrderAsync(GetUserId(), request);
        return CreatedAtAction(nameof(GetOrderById), new { id = result.Id }, result);
    }

    [HttpGet]
    [Route("api/v{version:apiVersion}/orders")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(PaginatedResponse<OrderSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _orderService.GetMyOrdersAsync(GetUserId(), page, pageSize);
        return Ok(result);
    }

    [HttpGet]
    [Route("api/v{version:apiVersion}/orders/{id:int}")]
    [Authorize(Roles = "Customer,Admin")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(int id)
    {
        var result = await _orderService.GetOrderByIdAsync(id, GetUserId(), IsAdmin());
        return Ok(result);
    }

    [HttpPut]
    [Route("api/v{version:apiVersion}/orders/{id:int}/cancel")]
    [Authorize(Roles = "Customer")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelOrder(int id)
    {
        var result = await _orderService.CancelOrderAsync(id, GetUserId());
        return Ok(result);
    }

    // Admin endpoints
    [HttpGet]
    [Route("api/v{version:apiVersion}/admin/orders")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(PaginatedResponse<OrderSummaryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var result = await _orderService.GetAllOrdersAsync(page, pageSize, status, from, to);
        return Ok(result);
    }

    [HttpPut]
    [Route("api/v{version:apiVersion}/admin/orders/{id:int}/status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var result = await _orderService.UpdateOrderStatusAsync(id, request);
        return Ok(result);
    }
}