using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class ExternalAuthService : IExternalAuthService
{
    private readonly AppDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;

    public ExternalAuthService(AppDbContext context, IJwtTokenService jwtTokenService)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<(string AccessToken, string RefreshToken)> HandleCallbackAsync(string provider, IEnumerable<Claim> claims)
    {
        // Step 1: Extract email and ProviderUserId from claims
        var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
            ?? throw new Exception("Email claim not found.");

        var providerUserId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? throw new Exception("ProviderUserId claim not found.");

        var providerEmail = email;

        // Step 2: Check if external login exists
        var externalLogin = await _context.UserExternalLogins
            .Include(el => el.User)
            .FirstOrDefaultAsync(el => el.Provider == provider && el.ProviderUserId == providerUserId);

        User user;

        if (externalLogin != null)
        {
            // External login exists, load linked user
            user = externalLogin.User;
        }
        else
        {
            // Step 3: Check if user exists with same email
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            if (existingUser != null)
            {
                user = existingUser;

                // Link external login to existing user
                _context.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Provider = provider,
                    ProviderUserId = providerUserId,
                    ProviderEmail = providerEmail,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                // Step 4: Create new user and external login
                user = new User
                {
                    FirstName = claims.FirstOrDefault(c => c.Type == ClaimTypes.GivenName)?.Value ?? string.Empty,
                    LastName = claims.FirstOrDefault(c => c.Type == ClaimTypes.Surname)?.Value ?? string.Empty,
                    Email = email,
                    PasswordHash = null,
                    Role = UserRole.Customer,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _context.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Provider = provider,
                    ProviderUserId = providerUserId,
                    ProviderEmail = providerEmail,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
        }

        // Step 5: Issue JWT + refresh token
        var accessToken = _jwtTokenService.GenerateAccessToken(user);

        var refreshToken = new RefreshToken
        {
            Token = Guid.NewGuid().ToString(),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return (accessToken, refreshToken.Token);
    }
}