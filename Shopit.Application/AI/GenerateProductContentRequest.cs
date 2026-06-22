namespace Shopit.Application.AI;

/// <summary>
/// Product information supplied by the caller to generate marketing content.
/// </summary>
public class GenerateProductContentRequest
{
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Specs { get; set; } = string.Empty;
}
