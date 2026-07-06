namespace Shopit.Application.DTOs.Reviews;

public class SubmitReviewRequest
{
    public int ProductId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}