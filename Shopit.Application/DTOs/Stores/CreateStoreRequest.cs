namespace Shopit.Application.DTOs.Stores;

public class CreateStoreRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
}
