namespace Shopit.Application.AI;

public class ProductContentResponse
{
    public string Description { get; set; } = string.Empty;

    public List<string> Features { get; set; } = [];

    public string SeoTitle { get; set; } = string.Empty;

    public string MetaDescription { get; set; } = string.Empty;
}