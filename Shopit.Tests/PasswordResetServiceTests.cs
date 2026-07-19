using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

public class PasswordResetServiceTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    // A no-op email mock for tests that don't need to capture the emitted code.
    private static Mock<IEmailService> CreateEmailMock()
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, string email = "user@test.com", string password = "OldPass123")
    {
        var user = new User
        {
            FirstName = "Test",
            LastName = "User",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "JwtSettings:SecretKey", "super_secret_key_for_testing_1234567890!!" },
            { "JwtSettings:Issuer", "TestIssuer" },
            { "JwtSettings:Audience", "TestAudience" },
            { "JwtSettings:ExpiryMinutes", "15" }
        }).Build();

    private static AuthService CreateAuthService(AppDbContext db) =>
        new(db, new JwtTokenService(CreateConfig()), CreateConfig(), new StoreService(db, Mock.Of<ICacheService>()));

    [Fact]
    public async Task RequestPasswordReset_KnownEmail_EmailsCodeAndStoresHashedToken()
    {
        var db = CreateDb();
        await SeedUserAsync(db);
        string? sentCode = null;
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendPasswordResetCodeAsync("user@test.com", It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);
        var service = new PasswordResetService(db, email.Object);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "user@test.com" });

        email.Verify(e => e.SendPasswordResetCodeAsync("user@test.com", It.IsAny<string>()), Times.Once);
        sentCode.Should().MatchRegex(@"^\d{6}$");

        var token = await db.PasswordResetTokens.SingleAsync();
        token.CodeHash.Should().NotBe(sentCode);                       // stored hashed, not raw
        BCrypt.Net.BCrypt.Verify(sentCode, token.CodeHash).Should().BeTrue();
        token.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        token.UsedAt.Should().BeNull();
    }

    [Fact]
    public async Task RequestPasswordReset_UnknownEmail_SendsNothingAndCreatesNoToken()
    {
        var db = CreateDb();
        var email = new Mock<IEmailService>();
        var service = new PasswordResetService(db, email.Object);

        // Should complete normally (no enumeration) even though the email is unknown.
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "ghost@test.com" });

        email.Verify(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        (await db.PasswordResetTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResetPassword_ValidCode_SetsNewPassword_OldRejected_NewAccepted()
    {
        var db = CreateDb();
        await SeedUserAsync(db);
        string? sentCode = null;
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);
        var service = new PasswordResetService(db, email.Object);
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "user@test.com" });

        await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = sentCode!,
            NewPassword = "NewPass456"
        });

        var auth = CreateAuthService(db);
        var login = async () => await auth.LoginAsync(new LoginRequest { Email = "user@test.com", Password = "NewPass456" });
        await login.Should().NotThrowAsync();

        var oldLogin = async () => await auth.LoginAsync(new LoginRequest { Email = "user@test.com", Password = "OldPass123" });
        await oldLogin.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ResetPassword_WrongCode_ThrowsValidationException()
    {
        var db = CreateDb();
        await SeedUserAsync(db);
        var service = new PasswordResetService(db, CreateEmailMock().Object);
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "user@test.com" });

        var act = async () => await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = "000000",
            NewPassword = "NewPass456"
        });

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ResetPassword_ExpiredCode_ThrowsValidationException()
    {
        var db = CreateDb();
        var user = await SeedUserAsync(db);
        var code = "123456";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 12),
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1)   // already expired
        });
        await db.SaveChangesAsync();
        var service = new PasswordResetService(db, CreateEmailMock().Object);

        var act = async () => await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = code,
            NewPassword = "NewPass456"
        });

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ResetPassword_AlreadyUsedCode_ThrowsValidationException()
    {
        var db = CreateDb();
        var user = await SeedUserAsync(db);
        var code = "123456";
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            CodeHash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 12),
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            UsedAt = DateTime.UtcNow.AddMinutes(-5)       // already consumed
        });
        await db.SaveChangesAsync();
        var service = new PasswordResetService(db, CreateEmailMock().Object);

        var act = async () => await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = code,
            NewPassword = "NewPass456"
        });

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ResetPassword_Success_RevokesActiveRefreshTokens()
    {
        var db = CreateDb();
        var user = await SeedUserAsync(db);
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = "active-rt",
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false
        });
        await db.SaveChangesAsync();

        string? sentCode = null;
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);
        var service = new PasswordResetService(db, email.Object);
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "user@test.com" });

        await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = sentCode!,
            NewPassword = "NewPass456"
        });

        var rt = await db.RefreshTokens.SingleAsync(t => t.Token == "active-rt");
        rt.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_CodeIsSingleUse_SecondAttemptFails()
    {
        var db = CreateDb();
        await SeedUserAsync(db);
        string? sentCode = null;
        var email = new Mock<IEmailService>();
        email.Setup(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, code) => sentCode = code)
            .Returns(Task.CompletedTask);
        var service = new PasswordResetService(db, email.Object);
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest { Email = "user@test.com" });

        await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = sentCode!,
            NewPassword = "NewPass456"
        });

        var reuse = async () => await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "user@test.com",
            Code = sentCode!,
            NewPassword = "AnotherPass789"
        });

        await reuse.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ResetPassword_UnknownEmail_ThrowsValidationException()
    {
        var db = CreateDb();
        var service = new PasswordResetService(db, CreateEmailMock().Object);

        var act = async () => await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Email = "ghost@test.com",
            Code = "123456",
            NewPassword = "NewPass456"
        });

        await act.Should().ThrowAsync<ValidationException>();
    }
}
