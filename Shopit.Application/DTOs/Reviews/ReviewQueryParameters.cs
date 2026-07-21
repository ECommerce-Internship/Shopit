namespace Shopit.Application.DTOs.Reviews;
public class ReviewQueryParameters
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Status { get; set; }
    public int? StoreId { get; set; }
    public string? Category { get; set; }
}
