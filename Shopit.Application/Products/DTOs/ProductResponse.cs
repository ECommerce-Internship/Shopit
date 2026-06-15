namespace Shopit.Application.Products.DTOs;

public class ProductResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    public int StockQuantity { get; set; }

    public double AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? SeoTitle { get; set; }
    public string? MetaDescription { get; set; }
    public List<string>? Features { get; set; }
}