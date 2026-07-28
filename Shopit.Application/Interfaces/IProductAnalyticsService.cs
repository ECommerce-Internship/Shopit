using Shopit.Application.DTOs.ProductAnalytics;

namespace Shopit.Application.Interfaces;

/// <summary>
/// Records product-interest signals (clicks and time spent) and reports them back to
/// the product's seller. Recording is open to anonymous visitors; reporting is scoped
/// to the owning seller (or an admin).
/// </summary>
public interface IProductAnalyticsService
{
    /// <summary>Records a click/view on a product. <paramref name="userId"/> is null for anonymous visitors.</summary>
    Task RecordClickAsync(int productId, int? userId, CancellationToken cancellationToken = default);

    /// <summary>Records the time a user spent on a product page. <paramref name="userId"/> is null for anonymous visitors.</summary>
    Task RecordTimeSpentAsync(int productId, RecordTimeSpentRequest request, int? userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns click stats for a product. A seller may only view products in a store they
    /// own; an admin may view any product.
    /// </summary>
    Task<ProductClickStatsResponse> GetClickStatsAsync(int productId, int userId, bool isAdmin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns time-spent stats for a product. A seller may only view products in a store
    /// they own; an admin may view any product.
    /// </summary>
    Task<ProductTimeSpentStatsResponse> GetTimeSpentStatsAsync(int productId, int userId, bool isAdmin, CancellationToken cancellationToken = default);
}
