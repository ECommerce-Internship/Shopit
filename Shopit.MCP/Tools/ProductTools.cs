using System.Text.Json;
using ModelContextProtocol.Server;
using ModelContextProtocol;
using Microsoft.Extensions.DependencyInjection;
using Shopit.Application.Interfaces;
using Shopit.Application.Products;
using System.ComponentModel;
using Shopit.Application.Products.DTOs;

namespace Shopit.MCP.Tools;

[McpServerToolType]
public class ProductTools
{
    private readonly IProductService _productService;

    public ProductTools(IProductService productService)
    {
        _productService = productService;
    }

    [McpServerTool, Description("Search products with optional filters")]
    public async Task<string> search_products(
        [Description("Search term")] string? search = null,
        [Description("Category ID filter")] int? categoryId = null,
        [Description("Minimum price")] decimal? minPrice = null,
        [Description("Maximum price")] decimal? maxPrice = null,
        [Description("Page number")] int pageNumber = 1,
        [Description("Page size")] int pageSize = 10)
    {
        var query = new ProductQueryParameters
        {
            Search = search,
            CategoryId = categoryId,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        var result = await _productService.GetAllAsync(query);
        return JsonSerializer.Serialize(result);
    }

    [McpServerTool, Description("Get a product by ID")]
    public async Task<string> get_product(
        [Description("Product ID")] int id)
    {
        try
        {
            var product = await _productService.GetByIdAsync(id);
            return JsonSerializer.Serialize(product);
        }
        catch (Exception ex)
        {
            throw new McpException($"Product with ID {id} not found: {ex.Message}");
        }
    }
}