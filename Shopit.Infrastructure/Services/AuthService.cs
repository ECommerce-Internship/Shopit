using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Auth;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Microsoft.Extensions.Configuration;

namespace Shopit.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext db, IJwtTokenService jwtTokenService, IConfiguration config)
    {
        _db = db;
        _jwtTokenService = jwtTokenService;
        _config = config;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _db.Users.AnyAsync(u => u.Email == request.Email))
            throw new ConflictException("Email is already registered.");

        var hash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

        var user = new User
        {
            Email = request.Email,
            PasswordHash = hash,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Role = UserRole.Customer
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var expiresIn = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15") * 60;
        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await _db.SaveChangesAsync();

        return BuildResponse(user, accessToken, refreshTokenValue, expiresIn);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == request.Email)
            ?? throw new UnauthorizedException("Invalid email or password.");

        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid email or password.");

        var accessToken = _jwtTokenService.GenerateAccessToken(user);
        var expiresIn = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15") * 60;
        var refreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await _db.SaveChangesAsync();

        return BuildResponse(user, accessToken, refreshTokenValue, expiresIn);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken)
    {
        var existing = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        if (existing.IsRevoked)
            throw new UnauthorizedException("Refresh token has been revoked.");

        if (existing.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedException("Refresh token has expired.");

        existing.IsRevoked = true;

        var accessToken = _jwtTokenService.GenerateAccessToken(existing.User);
        var expiresIn = int.Parse(_config["JwtSettings:ExpiryMinutes"] ?? "15") * 60;
        var newRefreshTokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefreshTokenValue,
            UserId = existing.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        });

        await _db.SaveChangesAsync();

        return BuildResponse(existing.User, accessToken, newRefreshTokenValue, expiresIn);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (existing is null || existing.IsRevoked)
            return;

        existing.IsRevoked = true;
        await _db.SaveChangesAsync();
    }

    private static AuthResponse BuildResponse(User user, string accessToken, string refreshToken, int expiresIn) =>
        new()
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
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