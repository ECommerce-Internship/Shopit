namespace Shopit.Application.AI;

/// <summary>
/// Performs AI-based content moderation on review comments using the Google Gemini API,
/// classifying them as genuine or as one of several fraud/abuse categories.
/// </summary>
public interface IReviewModerationService
{
    Task<ReviewModerationVerdict> ModerateReviewAsync(
        string comment,
        int rating,
        CancellationToken cancellationToken = default);
}
