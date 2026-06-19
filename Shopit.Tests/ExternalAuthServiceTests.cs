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

    private IConfiguration CreateConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "Shopit-Super-Secret-Key-That-Is-At-Least-32-Chars!!",
                ["JwtSettings:Issuer"] = "Shopit",
                ["JwtSettings:Audience"] = "ShopitUsers",
                ["JwtSettings:ExpiryMinutes"] = "15"
            })
            .Build();
    }

    private JwtTokenService CreateJwtTokenService(IConfiguration config)
    {
        return new JwtTokenService(config);
    }

    private IEnumerable<Claim> BuildGoogleClaims(string email, string providerUserId, string firstName = "John", string lastName = "Doe", bool emailVerified = true)
    {
        return new List<Claim>
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.NameIdentifier, providerUserId),
            new Claim(ClaimTypes.GivenName, firstName),
            new Claim(ClaimTypes.Surname, lastName),
            new Claim("email_verified", emailVerified.ToString().ToLower())
        };
    }

    [Fact]
    public async Task ExistingExternalLogin_ReturnsExistingUser()
    {
        var db = CreateDb();
        var config = CreateConfig();
        var jwtService = CreateJwtTokenService(config);

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

        var service = new ExternalAuthService(db, jwtService, config);
        var claims = BuildGoogleClaims("john@gmail.com", "google-123");

        var response = await service.HandleCallbackAsync("Google", claims);

        response.AccessToken.Should().NotBeNullOrEmpty();
        response.RefreshToken.Should().NotBeNullOrEmpty();
        response.ExpiresIn.Should().BeGreaterThan(0);
        response.User.Should().NotBeNull();
        response.User.Email.Should().Be("john@gmail.com");
    }

    [Fact]
    public async Task NewExternalLogin_ExistingEmail_LinksAndReturnsUser()
    {
        var db = CreateDb();
        var config = CreateConfig();
        var jwtService = CreateJwtTokenService(config);

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

        var service = new ExternalAuthService(db, jwtService, config);
        var claims = BuildGoogleClaims("jane@gmail.com", "google-456");

        var response = await service.HandleCallbackAsync("Google", claims);

        response.AccessToken.Should().NotBeNullOrEmpty();
        response.RefreshToken.Should().NotBeNullOrEmpty();
        response.User.Email.Should().Be("jane@gmail.com");

        var externalLogin = await db.UserExternalLogins
            .FirstOrDefaultAsync(el => el.ProviderUserId == "google-456");
        externalLogin.Should().NotBeNull();
        externalLogin!.UserId.Should().Be(user.Id);
    }

    [Fact]
    public async Task NewExternalLogin_NewEmail_CreatesUserAndReturnsJwt()
    {
        var db = CreateDb();
        var config = CreateConfig();
        var jwtService = CreateJwtTokenService(config);

        var service = new ExternalAuthService(db, jwtService, config);
        var claims = BuildGoogleClaims("newuser@gmail.com", "google-789");

        var response = await service.HandleCallbackAsync("Google", claims);

        response.AccessToken.Should().NotBeNullOrEmpty();
        response.RefreshToken.Should().NotBeNullOrEmpty();
        response.User.Email.Should().Be("newuser@gmail.com");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == "newuser@gmail.com");
        user.Should().NotBeNull();
        user!.Role.Should().Be(UserRole.Customer);
        user.PasswordHash.Should().BeNull();

        var externalLogin = await db.UserExternalLogins
            .FirstOrDefaultAsync(el => el.ProviderUserId == "google-789");
        externalLogin.Should().NotBeNull();
    }

    [Fact]
    public async Task NullEmail_ThrowsUnauthorizedException()
    {
        var db = CreateDb();
        var config = CreateConfig();
        var jwtService = CreateJwtTokenService(config);

        var service = new ExternalAuthService(db, jwtService, config);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "google-000")
            // No email claim
        };

        var act = async () => await service.HandleCallbackAsync("Google", claims);

        await act.Should().ThrowAsync<Shopit.Domain.Exceptions.UnauthorizedException>()
            .WithMessage("*Email claim not found*");
    }

    [Fact]
    public async Task ValidUser_JwtContainsCorrectRoleClaim()
    {
        var db = CreateDb();
        var config = CreateConfig();
        var jwtService = CreateJwtTokenService(config);

        var service = new ExternalAuthService(db, jwtService, config);
        var claims = BuildGoogleClaims("roletest@gmail.com", "google-role-123");

        var response = await service.HandleCallbackAsync("Google", claims);

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(response.AccessToken);

        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type == "role");
        roleClaim.Should().NotBeNull();
        roleClaim!.Value.Should().Be("Customer");

        response.User.Role.Should().Be("Customer");
    }
}