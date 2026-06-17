namespace Shopit.Application.DTOs.Reviews;

public class ProductReviewsResponse
{
    public int ProductId { get; set; }
    public double AverageRating { get; set; }
    public int TotalCount { get; set; }
    public List<ReviewResponse> Reviews { get; set; } = new();
}