using System.Text.Json;
using ModelContextProtocol.Server;
using Shopit.Application.Interfaces;
using System.ComponentModel;

namespace Shopit.MCP.Tools;

[McpServerToolType]
public class InventoryTools
{
    private readonly IInventoryService _inventoryService;

    public InventoryTools(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [McpServerTool, Description("Get all products with low stock levels")]
    public async Task<string> get_low_stock_products()
    {
        var result = await _inventoryService.GetLowStockAsync();
        return JsonSerializer.Serialize(result);
    }
}