using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
public class StoreOrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public StoreOrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() =>
        User.IsInRole("Admin");

    /// <summary>A seller's store orders (their portion of buyers' orders) across their stores.</summary>
    [HttpGet]
    [Route("api/v{version:apiVersion}/store-orders/mine")]
    [Authorize(Roles = "Seller")]
    [ProducesResponseType(typeof(IReadOnlyList<SellerStoreOrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMine()
    {
        var result = await _orderService.GetMyStoreOrdersAsync(GetUserId());
        return Ok(result);
    }

    [HttpGet]
    [Route("api/v{version:apiVersion}/store-orders/{id:int}")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(SellerStoreOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _orderService.GetStoreOrderByIdAsync(id, GetUserId(), IsAdmin());
        return Ok(result);
    }

    /// <summary>Advances the fulfillment status of a single store order (the seller's own portion).</summary>
    [HttpPut]
    [Route("api/v{version:apiVersion}/store-orders/{id:int}/status")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(SellerStoreOrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var result = await _orderService.UpdateStoreOrderStatusAsync(id, request, GetUserId(), IsAdmin());
        return Ok(result);
    }
}
