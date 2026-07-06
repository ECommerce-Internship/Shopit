using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shopit.Application.DTOs.Dashboard;
using Shopit.Application.Interfaces;

namespace Shopit.API.Controllers;

/// <summary>
/// Admin dashboard analytics endpoints.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/dashboard")]
[Authorize(Roles = "Admin")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Returns overall store summary: revenue, orders, customers, low stock, today's orders.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryResponse>> GetSummary()
    {
        var result = await _dashboardService.GetSummaryAsync();
        return Ok(result);
    }

    /// <summary>
    /// Returns revenue grouped by day, week, or month.
    /// </summary>
    [HttpGet("revenue")]
    [ProducesResponseType(typeof(IEnumerable<RevenueByPeriodResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RevenueByPeriodResponse>>> GetRevenue(
        [FromQuery] string period = "day")
    {
        var result = await _dashboardService.GetRevenueByPeriodAsync(period);
        return Ok(result);
    }

    /// <summary>
    /// Returns top 10 products by units sold.
    /// </summary>
    [HttpGet("top-products")]
    [ProducesResponseType(typeof(IEnumerable<TopProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<TopProductResponse>>> GetTopProducts()
    {
        var result = await _dashboardService.GetTopProductsAsync();
        return Ok(result);
    }

    /// <summary>
    /// Returns new customers grouped by day, week, or month.
    /// </summary>
    [HttpGet("new-customers")]
    [ProducesResponseType(typeof(IEnumerable<NewCustomersByPeriodResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<NewCustomersByPeriodResponse>>> GetNewCustomers(
        [FromQuery] string period = "day")
    {
        var result = await _dashboardService.GetNewCustomersAsync(period);
        return Ok(result);
    }

    /// <summary>
    /// Returns order counts grouped by status.
    /// </summary>
    [HttpGet("orders-by-status")]
    [ProducesResponseType(typeof(IEnumerable<OrdersByStatusResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OrdersByStatusResponse>>> GetOrdersByStatus()
    {
        var result = await _dashboardService.GetOrdersByStatusAsync();
        return Ok(result);
    }
}