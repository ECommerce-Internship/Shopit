namespace Shopit.Application.DTOs.ImageSearch;

/// <summary>
/// Summary of a catalog re-index run: how many products had images embedded,
/// how many were skipped (image URL unchanged since last run), how many failed
/// (e.g. image unreachable or Azure error), and how many stale rows were removed
/// (products whose image was cleared or that were deleted).
/// </summary>
public record ImageIndexResultDto(int Embedded, int Skipped, int Failed, int Removed);
