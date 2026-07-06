using System.Text.Json;
using ModelContextProtocol.Server;
using Shopit.Application.Interfaces;
using System.ComponentModel;
namespace Shopit.MCP.Tools;
[McpServerToolType]
public class OrderTools
{
    private readonly IOrderService _orderService;
    public OrderTools(IOrderService orderService)
    {
        _orderService = orderService;
    }
    [McpServerTool, Description("Get an order by ID")]
    public async Task<string> get_order(
        [Description("Order ID")] int orderId,
        [Description("User ID")] int userId)
    {
        var order = await _orderService.GetOrderByIdAsync(orderId, userId, isAdmin: true);
        return JsonSerializer.Serialize(order);
    }
    [McpServerTool, Description("Get orders for a customer")]
    public async Task<string> get_customer_orders(
        [Description("Customer user ID")] int userId,
        [Description("Page number")] int page = 1,
        [Description("Page size")] int pageSize = 10)
    {
        var orders = await _orderService.GetMyOrdersAsync(userId, page, pageSize);
        return JsonSerializer.Serialize(orders);
    }
    [McpServerTool, Description("Returns the orders placed by the current user")]
    public async Task<string> get_my_orders(
        [Description("ID of the user whose orders to retrieve")] int userId,
        [Description("Page number")] int page = 1,
        [Description("Page size")] int pageSize = 10)
    {
        var orders = await _orderService.GetMyOrdersAsync(userId, page, pageSize);
        return JsonSerializer.Serialize(orders);
    }
}
