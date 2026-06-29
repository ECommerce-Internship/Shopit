using System.Text.Json;
using ModelContextProtocol.Server;
using Shopit.Application.Interfaces;
using System.ComponentModel;

namespace Shopit.MCP.Tools;

[McpServerToolType]
public class DashboardTools
{
    private readonly IDashboardService _dashboardService;

    public DashboardTools(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [McpServerTool, Description("Get the platform-wide admin dashboard summary, including total platform commission")]
    public async Task<string> get_dashboard_summary()
    {
        var summary = await _dashboardService.GetSummaryAsync();
        return JsonSerializer.Serialize(summary);
    }

    [McpServerTool, Description("Get a seller's dashboard summary scoped to their stores: gross sales, commission, and net earnings")]
    public async Task<string> get_seller_dashboard_summary(
        [Description("Seller user ID")] int sellerUserId)
    {
        var summary = await _dashboardService.GetSellerSummaryAsync(sellerUserId);
        return JsonSerializer.Serialize(summary);
    }
}