using Microsoft.AspNetCore.Mvc;

namespace Shopit.Application.Products.DTOs;

public class ProductQueryParameters
{
    public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public string? StoreSlug { get; set; }
    public int? StoreId { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }

    // Minimum average review rating (1–5). Products with no reviews are excluded when set.
    public double? MinRating { get; set; }

    public string SortBy { get; set; } = "createdAt";

    [FromQuery(Name = "sortOrder")]
    public string SortDirection { get; set; } = "desc";

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}