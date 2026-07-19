using Microsoft.EntityFrameworkCore;
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

    public ReviewService(AppDbContext context)
    {
        _context = context;
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

        var totalCount = await _context.Reviews
            .CountAsync(r => r.ProductId == productId);

        var averageRating = totalCount > 0
            ? await _context.Reviews
                .Where(r => r.ProductId == productId)
                .AverageAsync(r => (double)r.Rating)
            : 0;

        var reviews = await _context.Reviews
            .Include(r => r.User)
            .Where(r => r.ProductId == productId)
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
        // Check product exists
        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && !p.IsDeleted);

        if (product is null)
            throw new NotFoundException($"Product with ID {request.ProductId} was not found.");

        // Purchase validation — must have a delivered order containing this product
        var hasPurchased = await _context.StoreOrderItems
            .AnyAsync(soi =>
                soi.ProductId == request.ProductId &&
                soi.StoreOrder.Order.UserId == currentUserId &&
                soi.StoreOrder.Status == OrderStatus.Delivered);

        if (!hasPurchased)
            throw new ForbiddenException("You can only review products you have received.");

        // Check not already reviewed
        var alreadyReviewed = await _context.Reviews
            .AnyAsync(r => r.ProductId == request.ProductId && r.UserId == currentUserId);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this product.");

        var review = new Review
        {
            ProductId = request.ProductId,
            UserId = currentUserId,
            Rating = request.Rating,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        };

        _context.Reviews.Add(review);
        await _context.SaveChangesAsync();

        Log.Information("Review submitted by User {UserId} for Product {ProductId}", currentUserId, request.ProductId);

        return await GetReviewWithUser(review.Id);
    }
public async Task<ProductReviewsResponse> GetAllReviewsAsync(ReviewQueryParameters parameters)
{
    var query = _context.Reviews
        .Include(r => r.User)
        .OrderByDescending(r => r.CreatedAt)
        .AsQueryable();

    var totalCount = await query.CountAsync();

    var reviews = await query
        .Skip((parameters.PageNumber - 1) * parameters.PageSize)
        .Take(parameters.PageSize)
        .Select(r => new ReviewResponse
        {
            Id = r.Id,
            ProductId = r.ProductId,
            UserId = r.UserId,
            ReviewerFirstName = r.User.FirstName,
            ReviewerLastName = r.User.LastName,
            Rating = r.Rating,
            Comment = r.Comment,
            CreatedAt = r.CreatedAt
        })
        .ToListAsync();

    return new ProductReviewsResponse
    {
        TotalCount = totalCount,
        Reviews = reviews
    };
}
    public async Task<ReviewResponse> UpdateReviewAsync(int reviewId, UpdateReviewRequest request, int currentUserId)
    {
        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == reviewId);

        if (review is null)
            throw new NotFoundException($"Review with ID {reviewId} was not found.");

        if (review.UserId != currentUserId)
            throw new ForbiddenException("You are not authorized to edit this review.");

        // 48-hour edit window
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
        CreatedAt = review.CreatedAt
    };
}