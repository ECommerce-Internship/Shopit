namespace Shopit.Domain.Entities;

public class PasswordResetToken
{
    public int Id { get; set; }

    // BCrypt hash of the short numeric code emailed to the user. The raw code is never persisted.
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    // Null while unused; stamped the moment the code is consumed, enforcing single-use.
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
