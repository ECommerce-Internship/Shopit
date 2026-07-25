namespace Shopit.Application.DTOs.ProductAnalytics;

public class ProductClickStatsResponse
{
    public int ProductId { get; set; }

    /// <summary>Total click/view events recorded for the product.</summary>
    public int TotalClicks { get; set; }

    /// <summary>Number of distinct authenticated users who clicked (anonymous clicks excluded).</summary>
    public int UniqueUsers { get; set; }
}
