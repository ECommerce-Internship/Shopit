using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/inventory")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool IsAdmin() =>
        User.IsInRole("Admin");

    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<InventoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll()
    {
        var result = await _inventoryService.GetAllAsync();
        return Ok(result);
    }

    [HttpGet("{productId:int}")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByProductId(int productId)
    {
        var result = await _inventoryService.GetByProductIdAsync(productId, GetUserId(), IsAdmin());
        return Ok(result);
    }

    [HttpPut("{productId:int}/stock")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStock(int productId, [FromBody] UpdateStockRequest request)
    {
        var result = await _inventoryService.UpdateStockAsync(productId, request.Quantity, GetUserId(), IsAdmin());
        return Ok(result);
    }

    [HttpGet("low-stock")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<InventoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLowStock()
    {
        var result = await _inventoryService.GetLowStockAsync();
        return Ok(result);
    }

    [HttpPut("{productId:int}/threshold")]
    [Authorize(Roles = "Seller,Admin")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateThreshold(int productId, [FromBody] UpdateThresholdRequest request)
    {
        var result = await _inventoryService.UpdateThresholdAsync(productId, request.Threshold, GetUserId(), IsAdmin());
        return Ok(result);
    }
}