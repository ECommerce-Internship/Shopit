namespace Shopit.Application.DTOs.ProductAnalytics;

public class ProductTimeSpentStatsResponse
{
    public int ProductId { get; set; }

    /// <summary>Sum of all recorded dwell times, in milliseconds.</summary>
    public long TotalDurationMs { get; set; }

    /// <summary>Average dwell time per recorded session, in milliseconds.</summary>
    public double AverageDurationMs { get; set; }

    /// <summary>Number of time-spent events that make up these figures.</summary>
    public int SampleCount { get; set; }
}
