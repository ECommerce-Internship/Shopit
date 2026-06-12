namespace Shopit.Application.Products.DTOs;

public class UpdateProductRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public int CategoryId { get; set; }

    public int StockQuantity { get; set; }
}