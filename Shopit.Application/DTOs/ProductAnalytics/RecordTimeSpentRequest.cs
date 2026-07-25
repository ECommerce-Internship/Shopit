namespace Shopit.Application.DTOs.ProductAnalytics;

public class RecordTimeSpentRequest
{
    /// <summary>Time the user spent on the product page, in milliseconds.</summary>
    public long DurationMs { get; set; }
}
