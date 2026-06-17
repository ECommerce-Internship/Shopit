using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using System.Security.Claims;
using Xunit;

namespace Shopit.Tests;

public class ExternalAuthServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private JwtTokenService CreateJwtTokenService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "Shopit-Super-Secret-Key-That-Is-At-Least-32-Chars!!",
                ["JwtSettings:Issuer"] = "Shopit",
                ["JwtSettings:Audience"] = "ShopitUsers",
                ["JwtSettings:ExpiryMinutes"] = "15"
            })
            .Build();
        return new JwtTokenService(config);
    }

    private IEnumerable<Claim> BuildGoogleClaims(string email, string providerUserId, string firstName = "John", string lastName = "Doe")
    {
        return new List<Claim>
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.NameIdentifier, providerUserId),
            new Claim(ClaimTypes.GivenName, firstName),
            new Claim(ClaimTypes.Surname, lastName)
        };
    }

    [Fact]
    public async Task ExistingExternalLogin_ReturnsExistingUser()
    {
        var db = CreateDb();
        var jwtService = CreateJwtTokenService();

        var user = new User
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@gmail.com",
            PasswordHash = null,
            Role = UserRole.Customer,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Provider = "Google",
            ProviderUserId = "google-123",
            ProviderEmail = "john@gmail.com",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ExternalAuthService(db, jwtService);
        var claims = BuildGoogleClaims("john@gmail.com", "google-123");

        var (accessToken, refreshToken) = await service.HandleCallbackAsync("Google", claims);

        accessToken.Should().NotBeNullOrEmpty();
        refreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NewExternalLogin_ExistingEmail_LinksAndReturnsUser()
    {
        var db = CreateDb();
        var jwtService = CreateJwtTokenService();

        var user = new User
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@gmail.com",
            PasswordHash = "existinghash",
            Role = UserRole.Customer,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var service = new ExternalAuthService(db, jwtService);
        var claims = BuildGoogleClaims("jane@gmail.com", "google-456");

        var (accessToken, refreshToken) = await service.HandleCallbackAsync("Google", claims);

        accessToken.Should().NotBeNullOrEmpty();
        refreshToken.Should().NotBeNullOrEmpty();

        var externalLogin = await db.UserExternalLogins
            .FirstOrDefaultAsync(el => el.ProviderUserId == "google-456");
        externalLogin.Should().NotBeNull();
        externalLogin!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task NewExternalLogin_NewEmail_CreatesUserAndReturnsJwt()
    {
        var db = CreateDb();
        var jwtService = CreateJwtTokenService();

        var service = new ExternalAuthService(db, jwtService);
        var claims = BuildGoogleClaims("newuser@gmail.com", "google-789");

        var (accessToken, refreshToken) = await service.HandleCallbackAsync("Google", claims);

        accessToken.Should().NotBeNullOrEmpty();
        refreshToken.Should().NotBeNullOrEmpty();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == "newuser@gmail.com");
        user.Should().NotBeNull();
        user!.Role.Should().Be(UserRole.Customer);
        user.PasswordHash.Should().BeNull();

        var externalLogin = await db.UserExternalLogins
            .FirstOrDefaultAsync(el => el.ProviderUserId == "google-789");
        externalLogin.Should().NotBeNull();
    }

    [Fact]
    public async Task NullEmail_ThrowsException()
    {
        var db = CreateDb();
        var jwtService = CreateJwtTokenService();

        var service = new ExternalAuthService(db, jwtService);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "google-000")
            // No email claim
        };

        var act = async () => await service.HandleCallbackAsync("Google", claims);

        await act.Should().ThrowAsync<Exception>()
            .WithMessage("*Email claim not found*");
    }

    [Fact]
    public async Task ValidUser_JwtContainsCorrectRoleClaim()
    {
        var db = CreateDb();
        var jwtService = CreateJwtTokenService();

        var service = new ExternalAuthService(db, jwtService);
        var claims = BuildGoogleClaims("roletest@gmail.com", "google-role-123");

        var (accessToken, _) = await service.HandleCallbackAsync("Google", claims);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role");
        roleClaim.Should().NotBeNull();
        roleClaim!.Value.Should().Be("Customer");
    }
}