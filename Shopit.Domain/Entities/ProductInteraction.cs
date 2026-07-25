using Shopit.Domain.Enums;

namespace Shopit.Domain.Entities;

/// <summary>
/// A single user-interest event on a product — either a click/view or a recorded
/// time-spent duration. Rows are append-only and power seller analytics that surface
/// products drawing interest (clicks or dwell time) out of proportion to their sales.
/// </summary>
public class ProductInteraction
{
    public long Id { get; set; }

    public ProductInteractionType Type { get; set; }

    /// <summary>
    /// Dwell time in milliseconds. Set only for <see cref="ProductInteractionType.TimeSpent"/>
    /// events; null for clicks.
    /// </summary>
    public long? DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>
    /// The authenticated user who generated the event, or null for an anonymous visitor.
    /// </summary>
    public int? UserId { get; set; }
    public User? User { get; set; }
}
