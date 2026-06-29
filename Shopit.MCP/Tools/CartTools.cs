using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using System.ComponentModel;

namespace Shopit.MCP.Tools;

[McpServerToolType]
public class CartTools
{
    private readonly ICartService _cartService;

    public CartTools(ICartService cartService)
    {
        _cartService = cartService;
    }

    [McpServerTool, Description("Adds a product to the current user's cart")]
    public async Task<string> add_to_cart(
        [Description("Product ID to add")] int productId,
        [Description("Quantity to add")] int quantity,
        [Description("ID of the user the cart belongs to")] int userId)
    {
        try
        {
            var request = new AddCartItemRequest
            {
                ProductId = productId,
                Quantity = quantity
            };
            var cart = await _cartService.AddItemAsync(userId, request);

            var itemCount = cart.Items.Sum(i => i.Quantity);
            return JsonSerializer.Serialize(new
            {
                message = $"Added to cart. Cart now has {itemCount} item(s).",
                cartTotal = cart.FinalTotal
            });
        }
        catch (Exception ex)
        {
            throw new McpException($"Could not add product {productId} to cart: {ex.Message}");
        }
    }

    [McpServerTool, Description("Returns the current user's cart contents and totals")]
    public async Task<string> view_cart(
        [Description("ID of the user the cart belongs to")] int userId)
    {
        var cart = await _cartService.GetCartAsync(userId);

        var items = cart.Items.Select(i => new
        {
            productName = i.ProductName,
            quantity = i.Quantity,
            unitPrice = i.UnitPrice,
            subtotal = i.Subtotal
        });

        return JsonSerializer.Serialize(new
        {
            items,
            subtotal = cart.Subtotal,
            discountAmount = cart.DiscountAmount,
            finalTotal = cart.FinalTotal
        });
    }
}
