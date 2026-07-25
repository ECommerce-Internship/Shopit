namespace Shopit.Application.AI;

/// <summary>
/// Structured verdict from AI-based content moderation of a review comment.
/// </summary>
public class ReviewModerationVerdict
{
    public bool IsSuspicious { get; set; }

    /// <summary>One of: genuine, spam, fake_promotional, toxic, incoherent, off_topic.</summary>
    public string Category { get; set; } = "genuine";

    public double Confidence { get; set; }

    public string? Reason { get; set; }
}
