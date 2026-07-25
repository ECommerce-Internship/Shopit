using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.ProductAnalytics;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using DomainValidationException = Shopit.Domain.Exceptions.ValidationException;

namespace Shopit.Infrastructure.Services;

public class ProductAnalyticsService : IProductAnalyticsService
{
    private readonly AppDbContext _context;
    private readonly IValidator<RecordTimeSpentRequest> _timeSpentValidator;

    public ProductAnalyticsService(
        AppDbContext context,
        IValidator<RecordTimeSpentRequest> timeSpentValidator)
    {
        _context = context;
        _timeSpentValidator = timeSpentValidator;
    }

    public async Task RecordClickAsync(int productId, int? userId, CancellationToken cancellationToken = default)
    {
        await EnsureProductExistsAsync(productId, cancellationToken);

        _context.ProductInteractions.Add(new ProductInteraction
        {
            ProductId = productId,
            UserId = userId,
            Type = ProductInteractionType.Click,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordTimeSpentAsync(int productId, RecordTimeSpentRequest request, int? userId, CancellationToken cancellationToken = default)
    {
        var validationResult = await _timeSpentValidator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
            throw new DomainValidationException(string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

        await EnsureProductExistsAsync(productId, cancellationToken);

        _context.ProductInteractions.Add(new ProductInteraction
        {
            ProductId = productId,
            UserId = userId,
            Type = ProductInteractionType.TimeSpent,
            DurationMs = request.DurationMs,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductClickStatsResponse> GetClickStatsAsync(int productId, int userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await EnsureCanViewProductAsync(productId, userId, isAdmin, cancellationToken);

        var clicks = _context.ProductInteractions
            .AsNoTracking()
            .Where(pi => pi.ProductId == productId && pi.Type == ProductInteractionType.Click);

        return new ProductClickStatsResponse
        {
            ProductId = productId,
            TotalClicks = await clicks.CountAsync(cancellationToken),
            UniqueUsers = await clicks
                .Where(pi => pi.UserId != null)
                .Select(pi => pi.UserId)
                .Distinct()
                .CountAsync(cancellationToken)
        };
    }

    public async Task<ProductTimeSpentStatsResponse> GetTimeSpentStatsAsync(int productId, int userId, bool isAdmin, CancellationToken cancellationToken = default)
    {
        await EnsureCanViewProductAsync(productId, userId, isAdmin, cancellationToken);

        var durations = _context.ProductInteractions
            .AsNoTracking()
            .Where(pi => pi.ProductId == productId
                && pi.Type == ProductInteractionType.TimeSpent
                && pi.DurationMs != null)
            .Select(pi => pi.DurationMs!.Value);

        var sampleCount = await durations.CountAsync(cancellationToken);

        return new ProductTimeSpentStatsResponse
        {
            ProductId = productId,
            SampleCount = sampleCount,
            // Averaging an empty sequence throws; short-circuit when there are no samples.
            TotalDurationMs = sampleCount == 0 ? 0 : await durations.SumAsync(cancellationToken),
            AverageDurationMs = sampleCount == 0 ? 0 : await durations.AverageAsync(cancellationToken)
        };
    }

    private async Task EnsureProductExistsAsync(int productId, CancellationToken cancellationToken)
    {
        var exists = await _context.Products
            .AnyAsync(p => p.Id == productId && !p.IsDeleted, cancellationToken);

        if (!exists)
            throw new NotFoundException($"Product with id {productId} was not found.");
    }

    // Interest analytics are private to the product's seller (and admins): a seller may
    // only view stats for products in a store they own.
    private async Task EnsureCanViewProductAsync(int productId, int userId, bool isAdmin, CancellationToken cancellationToken)
    {
        var ownerUserId = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == productId && !p.IsDeleted)
            .Select(p => (int?)p.Store.OwnerUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (ownerUserId is null)
            throw new NotFoundException($"Product with id {productId} was not found.");

        if (!isAdmin && ownerUserId.Value != userId)
            throw new ForbiddenException("You can only view analytics for products in your own stores.");
    }
}
