using Shopit.Domain.Enums;
namespace Shopit.Domain.Entities;
public class Review
{
    public int Id { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? ModerationReason { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public double? ModerationScore { get; set; }
}
