namespace Shopit.Application.DTOs.Auth;

public class RegisterSellerRequest
{
    public string FirstName { get; set; } = null!;
    public string LastName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string StoreName { get; set; } = null!;
    public string? StoreDescription { get; set; }
}
