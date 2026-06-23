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

    [McpServerTool, Description("Get dashboard summary with sales and inventory stats")]
    public async Task<string> get_dashboard_summary()
    {
        var summary = await _dashboardService.GetSummaryAsync();
        return JsonSerializer.Serialize(summary);
    }
}