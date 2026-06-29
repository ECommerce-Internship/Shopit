using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Dashboard;
using Shopit.Application.Interfaces;
using System.Security.Claims;

namespace Shopit.API.Controllers;

/// <summary>
/// Seller dashboard analytics, scoped to the authenticated seller's own store(s).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/seller/dashboard")]
[Authorize(Roles = "Seller")]
public class SellerDashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public SellerDashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>
    /// Returns the seller's summary: gross sales, commission, net earnings, orders, low stock, today's orders.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(SellerDashboardSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SellerDashboardSummaryResponse>> GetSummary()
    {
        var result = await _dashboardService.GetSellerSummaryAsync(GetUserId());
        return Ok(result);
    }

    /// <summary>
    /// Returns the seller's top 10 products by units sold.
    /// </summary>
    [HttpGet("top-products")]
    [ProducesResponseType(typeof(IEnumerable<TopProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<TopProductResponse>>> GetTopProducts()
    {
        var result = await _dashboardService.GetSellerTopProductsAsync(GetUserId());
        return Ok(result);
    }

    /// <summary>
    /// Returns the seller's gross revenue grouped by day, week, or month.
    /// </summary>
    [HttpGet("revenue")]
    [ProducesResponseType(typeof(IEnumerable<RevenueByPeriodResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<RevenueByPeriodResponse>>> GetRevenue(
        [FromQuery] string period = "day")
    {
        var result = await _dashboardService.GetSellerRevenueByPeriodAsync(GetUserId(), period);
        return Ok(result);
    }

    /// <summary>
    /// Returns the seller's store-order counts grouped by status.
    /// </summary>
    [HttpGet("orders-by-status")]
    [ProducesResponseType(typeof(IEnumerable<OrdersByStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<OrdersByStatusResponse>>> GetOrdersByStatus()
    {
        var result = await _dashboardService.GetSellerOrdersByStatusAsync(GetUserId());
        return Ok(result);
    }
}
