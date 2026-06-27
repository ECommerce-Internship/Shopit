using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Data;
using Xunit;

namespace Shopit.Tests;

public class DbInitializerTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ShopitSeed-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public void Seed_CreatesAdminSellerAndCustomerAccounts()
    {
        using var db = CreateDb();

        DbInitializer.Seed(db);

        db.Users.Count(u => u.Role == UserRole.Admin).Should().BeGreaterThanOrEqualTo(1);
        db.Users.Count(u => u.Role == UserRole.Seller).Should().BeGreaterThanOrEqualTo(2);
        db.Users.Count(u => u.Role == UserRole.Customer).Should().BeGreaterThanOrEqualTo(1);

        db.Users.Select(u => u.Email).Should().Contain(new[]
        {
            "admin@shopit.com", "seller1@shopit.com", "seller2@shopit.com", "customer@shopit.com"
        });
    }

    [Fact]
    public void Seed_EachSellerOwnsAnApprovedStore()
    {
        using var db = CreateDb();

        DbInitializer.Seed(db);

        var sellerIds = db.Users.Where(u => u.Role == UserRole.Seller).Select(u => u.Id).ToList();

        foreach (var sellerId in sellerIds)
        {
            db.Stores.Should().Contain(s =>
                s.OwnerUserId == sellerId && s.Status == StoreStatus.Approved);
        }
    }

    [Fact]
    public void Seed_AssignsEveryProductToAStore()
    {
        using var db = CreateDb();

        DbInitializer.Seed(db);

        db.Products.Should().NotBeEmpty();
        db.Products.Should().OnlyContain(p => p.StoreId != 0);
    }

    [Fact]
    public void Seed_IsIdempotent_WhenAlreadySeeded()
    {
        using var db = CreateDb();

        DbInitializer.Seed(db);
        var userCount = db.Users.Count();

        DbInitializer.Seed(db); // guarded: should be a no-op

        db.Users.Count().Should().Be(userCount);
    }
}
