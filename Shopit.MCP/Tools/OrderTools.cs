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

    [McpServerTool, Description("Get a seller's store orders (their portion of buyers' orders) across their stores, including commission and net amounts")]
    public async Task<string> get_seller_store_orders(
        [Description("Seller user ID")] int sellerUserId)
    {
        var storeOrders = await _orderService.GetMyStoreOrdersAsync(sellerUserId);
        return JsonSerializer.Serialize(storeOrders);
    }

    [McpServerTool, Description("Get a single store order by ID, including its items, commission, and seller net amount")]
    public async Task<string> get_store_order(
        [Description("Store order ID")] int storeOrderId)
    {
        var storeOrder = await _orderService.GetStoreOrderByIdAsync(storeOrderId, userId: 0, isAdmin: true);
        return JsonSerializer.Serialize(storeOrder);
    }
}