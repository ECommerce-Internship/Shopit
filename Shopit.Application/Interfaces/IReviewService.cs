using Shopit.Application.DTOs.Reviews;
namespace Shopit.Application.Interfaces;

public interface IReviewService
{
    Task<ProductReviewsResponse> GetByProductIdAsync(int productId, ReviewQueryParameters parameters);
    Task<ProductReviewsResponse> GetAllReviewsAsync(ReviewQueryParameters parameters);
    Task<ProductReviewsResponse> GetModerationQueueAsync(ReviewQueryParameters parameters);
    Task<ReviewResponse> SubmitReviewAsync(SubmitReviewRequest request, int currentUserId);
    Task<ReviewResponse> UpdateReviewAsync(int reviewId, UpdateReviewRequest request, int currentUserId);
    Task DeleteReviewAsync(int reviewId, int currentUserId);
    Task AdminDeleteReviewAsync(int reviewId);
    Task<ReviewResponse> ApproveReviewAsync(int reviewId);
    Task<ReviewResponse> RejectReviewAsync(int reviewId, RejectReviewRequest request);
}
