namespace Shopit.Application.AI;

/// <summary>
/// Strongly typed AI-generated product marketing content.
/// </summary>
public class ProductContentResponse
{
    public string Description { get; set; } = string.Empty;
    public List<string> Features { get; set; } = new();
    public string SeoTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
}
