using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Auth;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class PasswordResetService : IPasswordResetService
{
    // How long a reset code stays valid. Kept short (ticket asks for 10-15 min).
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;

    public PasswordResetService(AppDbContext db, IEmailService emailService)
    {
        _db = db;
        _emailService = emailService;
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email);

        // No account enumeration: for an unknown email we return normally without
        // creating a token or sending mail. The caller cannot distinguish this from success.
        if (user is null)
            return;

        // 6-digit numeric code, cryptographically random and zero-padded (e.g. "004217").
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        _db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 12),
            ExpiresAt = DateTime.UtcNow.Add(CodeLifetime)
        });
        await _db.SaveChangesAsync();

        await _emailService.SendPasswordResetCodeAsync(user.Email, code);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new ValidationException("Invalid or expired code.");

        // Newest still-valid (unused, unexpired) token for this user.
        var token = await _db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (token is null || !BCrypt.Net.BCrypt.Verify(request.Code, token.CodeHash))
            throw new ValidationException("Invalid or expired code.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
        token.UsedAt = DateTime.UtcNow;

        // A password reset invalidates every existing session: revoke the user's refresh tokens.
        var activeRefreshTokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == user.Id && !rt.IsRevoked)
            .ToListAsync();
        foreach (var rt in activeRefreshTokens)
            rt.IsRevoked = true;

        await _db.SaveChangesAsync();
    }
}
