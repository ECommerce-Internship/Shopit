namespace Shopit.Application.Products.DTOs;

public class CreateProductRequest
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    public int CategoryId { get; set; }

    public int StoreId { get; set; }

    public int InitialStock { get; set; }

    // The stock level at or below which the product is flagged as low on stock.
    // Defaults to 10 to preserve behaviour for callers (e.g. bulk import) that
    // don't supply it.
    public int LowStockThreshold { get; set; } = 10;
}