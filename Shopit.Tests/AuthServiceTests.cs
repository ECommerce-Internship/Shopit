using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Shopit.Application.DTOs.Auth;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class AuthServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private IConfiguration CreateConfig()
    {
        var settings = new Dictionary<string, string?>
        {
            { "JwtSettings:SecretKey", "super_secret_key_for_testing_1234567890!!" },
            { "JwtSettings:Issuer", "TestIssuer" },
            { "JwtSettings:Audience", "TestAudience" },
            { "JwtSettings:ExpiryMinutes", "15" }
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    private IJwtTokenService CreateJwtService()
    {
        var mock = new Mock<IJwtTokenService>();
        mock.Setup(j => j.GenerateAccessToken(It.IsAny<User>()))
            .Returns("mocked-access-token");
        return mock.Object;
    }

    private AuthService CreateService(AppDbContext db)
        => new AuthService(db, CreateJwtService(), CreateConfig());

    [Fact]
    public async Task RegisterAsync_ValidRequest_ReturnsAuthResponse()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var result = await service.RegisterAsync(new RegisterRequest
        {
            FirstName = "Maria",
            LastName = "Test",
            Email = "maria@test.com",
            Password = "Password123!"
        });

        result.Should().NotBeNull();
        result.User.Email.Should().Be("maria@test.com");
        result.User.Role.Should().Be(UserRole.Customer.ToString());
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsConflictException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var request = new RegisterRequest
        {
            FirstName = "Maria",
            LastName = "Test",
            Email = "duplicate@test.com",
            Password = "Password123!"
        };

        await service.RegisterAsync(request);

        var act = async () => await service.RegisterAsync(request);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already registered*");
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        var db = CreateDb();
        var service = CreateService(db);

        await service.RegisterAsync(new RegisterRequest
        {
            FirstName = "Maria",
            LastName = "Test",
            Email = "login@test.com",
            Password = "Password123!"
        });

        var result = await service.LoginAsync(new LoginRequest
        {
            Email = "login@test.com",
            Password = "Password123!"
        });

        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.User.Email.Should().Be("login@test.com");
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        await service.RegisterAsync(new RegisterRequest
        {
            FirstName = "Maria",
            LastName = "Test",
            Email = "wrongpass@test.com",
            Password = "Password123!"
        });

        var act = async () => await service.LoginAsync(new LoginRequest
        {
            Email = "wrongpass@test.com",
            Password = "WrongPassword!"
        });

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*Invalid email or password*");
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsUnauthorizedException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var user = new User
        {
            FirstName = "Maria",
            LastName = "Test",
            Email = "expired@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12),
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.RefreshTokens.Add(new RefreshToken
        {
            Token = "expired-token-value",
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            IsRevoked = false
        });
        await db.SaveChangesAsync();

        var act = async () => await service.RefreshTokenAsync("expired-token-value");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task RefreshTokenAsync_RevokedToken_ThrowsUnauthorizedException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var user = new User
        {
            FirstName = "Maria",
            LastName = "Test",
            Email = "revoked@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!", 12),
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.RefreshTokens.Add(new RefreshToken
        {
            Token = "revoked-token-value",
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = true
        });
        await db.SaveChangesAsync();

        var act = async () => await service.RefreshTokenAsync("revoked-token-value");

        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("*revoked*");
    }
}