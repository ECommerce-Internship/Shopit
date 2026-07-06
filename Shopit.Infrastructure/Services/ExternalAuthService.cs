using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shopit.Application.DTOs.Auth;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class ExternalAuthService : IExternalAuthService
{
    private readonly AppDbContext _context;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _config;

    public ExternalAuthService(AppDbContext context, IJwtTokenService jwtTokenService, IConfiguration config)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _config = config;
    }

    public async Task<AuthResponse> HandleCallbackAsync(string provider, IEnumerable<Claim> claims)
    {
        // Step 1: Extract email and ProviderUserId from claims
        var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value
            ?? throw new UnauthorizedException("Email claim not found.");

        var providerUserId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedException("ProviderUserId claim not found.");

        // Check email_verified claim to prevent account-takeover via unverified email
        var emailVerifiedClaim = claims.FirstOrDefault(c => c.Type == "email_verified")?.Value;
        var emailVerified = string.IsNullOrEmpty(emailVerifiedClaim) || bool.Parse(emailVerifiedClaim);

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
            if (!emailVerified)
                throw new UnauthorizedException("Email is not verified by the provider.");

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
        var storeIds = user.Role == UserRole.Seller
            ? await _context.Stores.Where(s => s.OwnerUserId == user.Id).Select(s => s.Id).ToListAsync()
            : new List<int>();
        var accessToken = _jwtTokenService.GenerateAccessToken(user, storeIds);
        var expiresIn = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15") * 60;

        var refreshToken = new RefreshToken
        {
            Token = Guid.NewGuid().ToString(),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresIn = expiresIn,
            User = new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role.ToString()
            }
        };
    }
}