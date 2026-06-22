namespace Shopit.Domain.Entities;

public class UserExternalLogin
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderUserId { get; set; } = string.Empty;
    public string ProviderEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation property
    public User User { get; set; } = null!;
}