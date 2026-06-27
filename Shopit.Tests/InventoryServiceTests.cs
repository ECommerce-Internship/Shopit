using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class InventoryServiceTests
{
    private const int OwnerUserId = 1;
    private const int OtherUserId = 99;
    private const int ProductId = 1;

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static InventoryService CreateService(AppDbContext db) =>
        new(db, Mock.Of<ILowStockAlertService>());

    // Seeds: user 1 owns store 1, which owns product 1 (with inventory).
    private static async Task SeedAsync(AppDbContext db)
    {
        db.Users.Add(new User { Id = OwnerUserId, FirstName = "Sal", LastName = "Seller", Email = "s@t.com", Role = UserRole.Seller });
        db.Categories.Add(new Category { Id = 1, Name = "Cat", Slug = "cat" });
        db.Stores.Add(new Store { Id = 1, Name = "Store", Slug = "store", Status = StoreStatus.Approved, OwnerUserId = OwnerUserId });
        db.Products.Add(new Product
        {
            Id = ProductId, Name = "Widget", SKU = "W-1", Price = 9.99m, CategoryId = 1, StoreId = 1,
            CreatedAt = DateTime.UtcNow, IsDeleted = false,
            Inventory = new Inventory { Quantity = 10, LowStockThreshold = 2 }
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateStock_NonOwner_ThrowsForbidden()
    {
        using var db = CreateDb();
        await SeedAsync(db);

        var act = async () => await CreateService(db).UpdateStockAsync(ProductId, 5, OtherUserId, isAdmin: false);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task UpdateStock_Owner_Succeeds()
    {
        using var db = CreateDb();
        await SeedAsync(db);

        var result = await CreateService(db).UpdateStockAsync(ProductId, 7, OwnerUserId, isAdmin: false);

        result.Quantity.Should().Be(7);
    }

    [Fact]
    public async Task UpdateStock_Admin_Succeeds()
    {
        using var db = CreateDb();
        await SeedAsync(db);

        var result = await CreateService(db).UpdateStockAsync(ProductId, 3, OtherUserId, isAdmin: true);

        result.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task UpdateThreshold_NonOwner_ThrowsForbidden()
    {
        using var db = CreateDb();
        await SeedAsync(db);

        var act = async () => await CreateService(db).UpdateThresholdAsync(ProductId, 5, OtherUserId, isAdmin: false);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetByProductId_NonOwner_ThrowsForbidden()
    {
        using var db = CreateDb();
        await SeedAsync(db);

        var act = async () => await CreateService(db).GetByProductIdAsync(ProductId, OtherUserId, isAdmin: false);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetByProductId_Owner_Succeeds()
    {
        using var db = CreateDb();
        await SeedAsync(db);

        var result = await CreateService(db).GetByProductIdAsync(ProductId, OwnerUserId, isAdmin: false);

        result.ProductId.Should().Be(ProductId);
    }
}
