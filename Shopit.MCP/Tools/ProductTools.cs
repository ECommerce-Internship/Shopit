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

    // Seller-only. The caller's userId is injected from their JWT by ChatService
    // (identity injection) and hidden from the model, so a seller can only create
    // products in a store they own — isAdmin is always false here.
    [McpServerTool, Description("Creates a new product in one of the seller's own stores")]
    public async Task<string> create_product(
        [Description("Product name")] string name,
        [Description("Price in the store currency")] decimal price,
        [Description("Unique stock-keeping unit (SKU)")] string sku,
        [Description("ID of the category the product belongs to")] int categoryId,
        [Description("ID of the seller's store to create the product in")] int storeId,
        [Description("Initial stock quantity")] int initialStock,
        [Description("ID of the seller creating the product")] int userId,
        [Description("Optional product description")] string? description = null,
        [Description("Stock level at or below which the product is flagged low on stock")] int lowStockThreshold = 10)
    {
        try
        {
            var request = new CreateProductRequest
            {
                Name = name,
                Price = price,
                Sku = sku,
                CategoryId = categoryId,
                StoreId = storeId,
                InitialStock = initialStock,
                Description = description,
                LowStockThreshold = lowStockThreshold
            };

            var product = await _productService.CreateAsync(request, userId, isAdmin: false);
            return JsonSerializer.Serialize(new
            {
                message = $"Product '{product.Name}' created successfully.",
                productId = product.Id,
                sku = product.Sku
            });
        }
        catch (Exception ex)
        {
            throw new McpException($"Could not create product: {ex.Message}");
        }
    }

    // Seller-only. Like create_product, userId is injected from the caller's JWT and
    // hidden from the model; the service rejects edits to products the seller does
    // not own (isAdmin is always false here).
    [McpServerTool, Description("Updates an existing product in one of the seller's own stores")]
    public async Task<string> update_product(
        [Description("ID of the product to update")] int id,
        [Description("Product name")] string name,
        [Description("Price in the store currency")] decimal price,
        [Description("Unique stock-keeping unit (SKU)")] string sku,
        [Description("ID of the category the product belongs to")] int categoryId,
        [Description("Stock quantity on hand")] int stockQuantity,
        [Description("ID of the seller who owns the product")] int userId,
        [Description("Optional product description")] string? description = null)
    {
        try
        {
            var request = new UpdateProductRequest
            {
                Name = name,
                Price = price,
                Sku = sku,
                CategoryId = categoryId,
                StockQuantity = stockQuantity,
                Description = description
            };

            var product = await _productService.UpdateAsync(id, request, userId, isAdmin: false);
            return JsonSerializer.Serialize(new
            {
                message = $"Product '{product.Name}' updated successfully.",
                productId = product.Id,
                sku = product.Sku
            });
        }
        catch (Exception ex)
        {
            throw new McpException($"Could not update product {id}: {ex.Message}");
        }
    }
}