using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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
        mock.Setup(j => j.GenerateAccessToken(It.IsAny<User>(), It.IsAny<IEnumerable<int>>()))
            .Returns("mocked-access-token");
        return mock.Object;
    }

    private AuthService CreateService(AppDbContext db)
        => new AuthService(db, CreateJwtService(), CreateConfig(), new StoreService(db, Mock.Of<ICacheService>()));

    // Builds AuthService with a REAL JwtTokenService so issued tokens can be decoded (SCRUM-144).
    private AuthService CreateServiceRealJwt(AppDbContext db)
        => new AuthService(db, new JwtTokenService(CreateConfig()), CreateConfig(), new StoreService(db, Mock.Of<ICacheService>()));

    private static List<int> DecodeStoreIds(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        return jwt.Claims.Where(c => c.Type == "StoreIds").Select(c => int.Parse(c.Value)).ToList();
    }

    [Fact]
    public async Task RegisterSellerAsync_TokenCarriesNewStoreId()
    {
        var db = CreateDb();
        var service = CreateServiceRealJwt(db);

        var result = await service.RegisterSellerAsync(new RegisterSellerRequest
        {
            FirstName = "S", LastName = "S", Email = "claim-seller@s.com", Password = "Password123!", StoreName = "Claim Store"
        });

        var store = await db.Stores.SingleAsync(s => s.OwnerUserId == result.User.Id);
        DecodeStoreIds(result.AccessToken).Should().Contain(store.Id);
    }

    [Fact]
    public async Task RegisterAsync_Customer_TokenHasEmptyStoreIds()
    {
        var db = CreateDb();
        var service = CreateServiceRealJwt(db);

        var result = await service.RegisterAsync(new RegisterRequest
        {
            FirstName = "C", LastName = "C", Email = "claim-customer@s.com", Password = "Password123!"
        });

        DecodeStoreIds(result.AccessToken).Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterSellerAsync_ValidRequest_CreatesSellerAndPendingStore()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var result = await service.RegisterSellerAsync(new RegisterSellerRequest
        {
            FirstName = "Sam",
            LastName = "Seller",
            Email = "sam@store.com",
            Password = "Password123!",
            StoreName = "Sam's Shop",
            StoreDescription = "Best deals"
        });

        result.Should().NotBeNull();
        result.User.Role.Should().Be(UserRole.Seller.ToString());
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();

        var user = await db.Users.SingleAsync(u => u.Email == "sam@store.com");
        user.Role.Should().Be(UserRole.Seller);

        var store = await db.Stores.SingleAsync(s => s.OwnerUserId == user.Id);
        store.Status.Should().Be(StoreStatus.Pending);
        store.Name.Should().Be("Sam's Shop");
    }

    [Fact]
    public async Task RegisterSellerAsync_DuplicateEmail_ThrowsConflictException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var request = new RegisterSellerRequest
        {
            FirstName = "Sam",
            LastName = "Seller",
            Email = "dupe-seller@store.com",
            Password = "Password123!",
            StoreName = "Sam's Shop"
        };

        await service.RegisterSellerAsync(request);

        var act = async () => await service.RegisterSellerAsync(request);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already registered*");
    }

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