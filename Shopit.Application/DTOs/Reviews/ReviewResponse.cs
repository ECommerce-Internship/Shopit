namespace Shopit.Application.DTOs.Reviews;
public class ReviewResponse
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int UserId { get; set; }
    public string ReviewerFirstName { get; set; } = string.Empty;
    public string ReviewerLastName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ModerationReason { get; set; }
    public DateTime? ModeratedAt { get; set; }
}
