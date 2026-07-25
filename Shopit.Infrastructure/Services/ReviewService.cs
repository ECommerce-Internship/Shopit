using Microsoft.EntityFrameworkCore;
using Shopit.Application.AI;
using Serilog;
using Shopit.Application.DTOs.Reviews;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class ReviewService : IReviewService
{
    private readonly AppDbContext _context;
    private readonly IReviewModerationService _moderationService;

    public ReviewService(AppDbContext context, IReviewModerationService moderationService)
    {
        _context = context;
        _moderationService = moderationService;
    }

    public async Task<ProductReviewsResponse> GetByProductIdAsync(int productId, ReviewQueryParameters parameters)
    {
        var pageNumber = parameters.PageNumber <= 0 ? 1 : parameters.PageNumber;
        var pageSize = parameters.PageSize <= 0 ? 10 : parameters.PageSize;
        if (pageSize > 100) pageSize = 100;

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId && !p.IsDeleted);

        if (product is null)
            throw new NotFoundException($"Product with ID {productId} was not found.");

        var baseQuery = _context.Reviews
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved);

        var totalCount = await baseQuery.CountAsync();

        var averageRating = totalCount > 0
            ? await baseQuery.AverageAsync(r => (double)r.Rating)
            : 0;

        var reviews = await baseQuery
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ProductReviewsResponse
        {
            ProductId = productId,
            AverageRating = Math.Round(averageRating, 2),
            TotalCount = totalCount,
            Reviews = reviews.Select(MapToResponse).ToList()
        };
    }

    public async Task<ReviewResponse> SubmitReviewAsync(SubmitReviewRequest request, int currentUserId)
    {
        var product = await _context.Products
            .Include(p => p.Store)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted);

        if (product is null)
            throw new NotFoundException($"Product with ID {request.ProductId} was not found.");

        var hasPurchased = await _context.StoreOrderItems
            .AnyAsync(soi =>
                soi.ProductId == request.ProductId &&
                soi.StoreOrder.Order.UserId == currentUserId &&
                soi.StoreOrder.Status == OrderStatus.Delivered);

        if (!hasPurchased)
            throw new ForbiddenException("You can only review products you have received.");

        var alreadyReviewed = await _context.Reviews
            .AnyAsync(r => r.ProductId == request.ProductId && r.UserId == currentUserId);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this product.");

        var user = await _context.Users.FirstAsync(u => u.Id == currentUserId);

        var (ruleFlagged, ruleReason) = await RunRuleBasedSignalsAsync(product, user, request);
        var (finalStatus, moderationReason, moderationScore, moderationCategory) = await DetermineFinalStatusAsync(ruleFlagged, ruleReason, request);

        var review = new Review
        {
            ProductId = request.ProductId,
            UserId = currentUserId,
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow,
            Status = finalStatus,
            ModerationReason = moderationReason,
            ModerationScore = moderationScore,
            ModerationCategory = moderationCategory,
            ModeratedAt = finalStatus == ReviewStatus.Pending ? null : DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        Log.Information("Review submitted by User {UserId} for Product {ProductId} (Status={Status}, Reason={Reason})", currentUserId, request.ProductId, review.Status, moderationReason ?? "none");

        return await GetReviewWithUser(review.Id);
    }

    // Combines the rule-based verdict with AI content moderation (when the rules found
    // nothing and there is comment text to assess) into the review's final status.
    // Gemini failures fail open to Flagged rather than blocking submission or silently
    // publishing unmoderated content. ModerationScore reflects rule certainty (1.0) or
    // the AI's confidence in its verdict, so admins can gauge how sure the pipeline was.
    // ModerationCategory is the AI's structured category (or null for rule-based flags
    // and for auto-approved content with no comment/no signal).
    private async Task<(ReviewStatus status, string? reason, double? score, string? category)> DetermineFinalStatusAsync(
        bool ruleFlagged, string? ruleReason, SubmitReviewRequest request)
    {
        if (ruleFlagged)
            return (ReviewStatus.Flagged, ruleReason, 1.0, null);

        if (string.IsNullOrWhiteSpace(request.Comment))
            return (ReviewStatus.Approved, null, null, null);

        try
        {
            var verdict = await _moderationService.ModerateReviewAsync(request.Comment, request.Rating);
            return verdict.IsSuspicious
                ? (ReviewStatus.Flagged, $"{verdict.Category}: {verdict.Reason}", verdict.Confidence, verdict.Category)
                : (ReviewStatus.Approved, (string?)null, verdict.Confidence, verdict.Category);
        }
        catch (ExternalServiceException ex)
        {
            Log.Warning(ex, "AI review moderation unavailable; failing open to Flagged.");
            return (ReviewStatus.Flagged, "AI moderation unavailable; flagged for manual review.", null, null);
        }
    }

    // Cheap, rule-based fraud signals. Runs before any AI call and short-circuits on the
    // first match, so a single obvious signal never waits on an external Gemini call.
    private async Task<(bool isSuspicious, string? reason)> RunRuleBasedSignalsAsync(Product product, User user, SubmitReviewRequest request)
    {
        const int burstWindowMinutes = 10;
        const int maxProductReviewsInWindow = 5;
        const int maxUserReviewsInWindow = 3;
        const int newAccountMinutes = 5;
        const int lowRatingThreshold = 2;
        const int bombingWindowMinutes = 30;
        const int bombingCountThreshold = 4;

        // Self-review / seller collusion
        if (product.Store.OwnerUserId == user.Id)
            return (true, "Self-review: reviewer owns the store selling this product.");

        var now = DateTime.UtcNow;

        // Very new account posting immediately after registration
        if (now - user.CreatedAt < TimeSpan.FromMinutes(newAccountMinutes))
            return (true, "New account posting a review shortly after registration.");

        // Burst of reviews on this product
        var recentProductReviews = await _context.Reviews
            .CountAsync(r => r.ProductId == product.Id && r.CreatedAt > now.AddMinutes(-burstWindowMinutes));
        if (recentProductReviews >= maxProductReviewsInWindow)
            return (true, "Unusual burst of reviews detected for this product.");

        // Burst of reviews from this reviewer
        var recentUserReviews = await _context.Reviews
            .CountAsync(r => r.UserId == user.Id && r.CreatedAt > now.AddMinutes(-burstWindowMinutes));
        if (recentUserReviews >= maxUserReviewsInWindow)
            return (true, "Unusual burst of reviews detected from this reviewer.");

        // Duplicate / near-duplicate comment text (exact match, case-insensitive, trimmed)
        if (!string.IsNullOrWhiteSpace(request.Comment))
        {
            var normalized = request.Comment.Trim().ToLowerInvariant();
            var isDuplicate = await _context.Reviews
                .Where(r => r.Comment != null)
                .AnyAsync(r => r.Comment!.Trim().ToLower() == normalized);
            if (isDuplicate)
                return (true, "Duplicate review text detected.");
        }

        // Competitor-bombing heuristic: burst of low ratings on this seller's store
        if (request.Rating <= lowRatingThreshold)
        {
            var recentLowRatingsForStore = await _context.Reviews
                .Include(r => r.Product)
                .CountAsync(r =>
                    r.Product.StoreId == product.StoreId &&
                    r.Rating <= lowRatingThreshold &&
                    r.CreatedAt > now.AddMinutes(-bombingWindowMinutes));
            if (recentLowRatingsForStore >= bombingCountThreshold)
                return (true, "Burst of low ratings for this store detected (possible competitor bombing).");
        }

        return (false, null);
    }

    // Applies the optional Status/StoreId/Category filters shared by GetAllReviewsAsync,
    // GetModerationQueueAsync, and GetFlaggedForSellerAsync.
    private static IQueryable<Review> ApplyFilters(IQueryable<Review> query, ReviewQueryParameters parameters)
    {
        if (!string.IsNullOrWhiteSpace(parameters.Status) &&
            Enum.TryParse<ReviewStatus>(parameters.Status, true, out var statusFilter))
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        if (parameters.StoreId.HasValue)
        {
            query = query.Where(r => r.Product.StoreId == parameters.StoreId.Value);
        }

        if (!string.IsNullOrWhiteSpace(parameters.Category))
        {
            query = query.Where(r => r.ModerationCategory == parameters.Category);
        }

        return query;
    }

    public async Task<ProductReviewsResponse> GetAllReviewsAsync(ReviewQueryParameters parameters)
    {
        var query = ApplyFilters(
            _context.Reviews.Include(r => r.User).Include(r => r.Product),
            parameters)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await query.CountAsync();

        var reviews = await query
            .Skip((parameters.PageNumber - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .ToListAsync();

        return new ProductReviewsResponse
        {
            TotalCount = totalCount,
            Reviews = reviews.Select(MapToResponse).ToList()
        };
    }

    public async Task<ProductReviewsResponse> GetModerationQueueAsync(ReviewQueryParameters parameters)
    {
        var pageNumber = parameters.PageNumber <= 0 ? 1 : parameters.PageNumber;
        var pageSize = parameters.PageSize <= 0 ? 10 : parameters.PageSize;
        if (pageSize > 100) pageSize = 100;

        var query = ApplyFilters(
            _context.Reviews.Include(r => r.User).Include(r => r.Product).Where(r => r.Status == ReviewStatus.Flagged),
            parameters)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await query.CountAsync();

        var reviews = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ProductReviewsResponse
        {
            TotalCount = totalCount,
            Reviews = reviews.Select(MapToResponse).ToList()
        };
    }

    public async Task<ProductReviewsResponse> GetFlaggedForSellerAsync(int sellerUserId, ReviewQueryParameters parameters)
    {
        var pageNumber = parameters.PageNumber <= 0 ? 1 : parameters.PageNumber;
        var pageSize = parameters.PageSize <= 0 ? 10 : parameters.PageSize;
        if (pageSize > 100) pageSize = 100;

        var query = ApplyFilters(
            _context.Reviews
                .Include(r => r.User)
                .Include(r => r.Product)
                .Where(r => r.Product.Store.OwnerUserId == sellerUserId &&
                            (r.Status == ReviewStatus.Flagged || r.Status == ReviewStatus.Rejected)),
            parameters)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        var totalCount = await query.CountAsync();

        var reviews = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new ProductReviewsResponse
        {
            TotalCount = totalCount,
            Reviews = reviews.Select(MapToResponse).ToList()
        };
    }

    public async Task<ReviewResponse> ApproveReviewAsync(int reviewId)
    {
        var review = await _context.Reviews
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        review.Status = ReviewStatus.Approved;
        review.ModeratedAt = DateTime.UtcNow;
        review.ModerationReason = null;

        await _context.SaveChangesAsync();

        Log.Information("Review {ReviewId} approved by admin", reviewId);

        return MapToResponse(review);
    }

    public async Task<ReviewResponse> RejectReviewAsync(int reviewId, RejectReviewRequest request)
    {
        var review = await _context.Reviews
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        review.Status = ReviewStatus.Rejected;
        review.ModeratedAt = DateTime.UtcNow;
        review.ModerationReason = request.Reason;

        await _context.SaveChangesAsync();

        Log.Information("Review {ReviewId} rejected by admin", reviewId);

        return MapToResponse(review);
    }

    public async Task<ReviewResponse> UpdateReviewAsync(int reviewId, UpdateReviewRequest request, int currentUserId)
    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        if (review.UserId != currentUserId)
            throw new ForbiddenException("You are not authorized to edit this review.");

        if (DateTime.UtcNow - review.CreatedAt > TimeSpan.FromHours(48))
            throw new Shopit.Domain.Exceptions.ValidationException("Reviews can only be edited within 48 hours of submission.");

        review.Rating = request.Rating;
        review.Comment = request.Comment;

        await _context.SaveChangesAsync();

        Log.Information("Review {ReviewId} updated by User {UserId}", reviewId, currentUserId);

        return await GetReviewWithUser(reviewId);
    }

    public async Task DeleteReviewAsync(int reviewId, int currentUserId)
    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        if (review.UserId != currentUserId)
            throw new ForbiddenException("You are not authorized to delete this review.");

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();

        Log.Information("Review {ReviewId} deleted by User {UserId}", reviewId, currentUserId);
    }

    public async Task AdminDeleteReviewAsync(int reviewId)
    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        _context.Reviews.Remove(review);
        await _context.SaveChangesAsync();

        Log.Information("Review {ReviewId} admin-deleted", reviewId);
    }

    private async Task<ReviewResponse> GetReviewWithUser(int reviewId)
    {
        var review = await _context.Reviews
            .Include(r => r.User)
            .FirstAsync(r => r.Id == reviewId);

        return MapToResponse(review);
    }

    private static ReviewResponse MapToResponse(Review review) => new()
    {
        Id = review.Id,
        ProductId = review.ProductId,
        UserId = review.UserId,
        ReviewerFirstName = review.User?.FirstName ?? string.Empty,
        ReviewerLastName = review.User?.LastName ?? string.Empty,
        Rating = review.Rating,
        Comment = review.Comment,
        CreatedAt = review.CreatedAt,
        Status = review.Status.ToString(),
        ModerationReason = review.ModerationReason,
        ModerationCategory = review.ModerationCategory,
        ModeratedAt = review.ModeratedAt,
        ModerationScore = review.ModerationScore
    };
}
