namespace Shopit.Application.AI;

public class GenerateProductContentRequest
{
    public string ProductName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Specs { get; set; } = string.Empty;

    public decimal Price { get; set; }
}