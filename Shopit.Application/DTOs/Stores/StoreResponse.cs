namespace Shopit.Application.DTOs.Stores;

public class StoreResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal CommissionRate { get; set; }
    public int OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
