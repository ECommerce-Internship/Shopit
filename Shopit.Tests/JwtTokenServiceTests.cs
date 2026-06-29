using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class JwtTokenServiceTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "JwtSettings:SecretKey", "super_secret_key_for_testing_1234567890!!" },
            { "JwtSettings:Issuer", "TestIssuer" },
            { "JwtSettings:Audience", "TestAudience" },
            { "JwtSettings:ExpiryMinutes", "15" }
        }).Build();

    private static List<int> StoreIds(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .Where(c => c.Type == "StoreIds")
            .Select(c => int.Parse(c.Value))
            .ToList();

    [Fact]
    public void GenerateAccessToken_Seller_IncludesStoreIdsClaim()
    {
        var service = new JwtTokenService(Config());
        var user = new User { Id = 5, Email = "seller@x.com", Role = UserRole.Seller };

        var token = service.GenerateAccessToken(user, new[] { 2, 4 });

        StoreIds(token).Should().BeEquivalentTo(new[] { 2, 4 });
    }

    [Fact]
    public void GenerateAccessToken_EmptyStoreIds_HasNoStoreIdsClaim()
    {
        var service = new JwtTokenService(Config());
        var user = new User { Id = 1, Email = "customer@x.com", Role = UserRole.Customer };

        var token = service.GenerateAccessToken(user, Array.Empty<int>());

        StoreIds(token).Should().BeEmpty();
    }
}
