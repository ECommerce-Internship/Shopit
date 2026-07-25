using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.ProductAnalytics;
using Shopit.Application.Validators;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;
using DomainValidationException = Shopit.Domain.Exceptions.ValidationException;

namespace Shopit.Tests;

public class ProductAnalyticsServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private ProductAnalyticsService CreateService(AppDbContext db)
    {
        IValidator<RecordTimeSpentRequest> validator = new RecordTimeSpentRequestValidator();
        return new ProductAnalyticsService(db, validator);
    }

    // Seeds a seller-owned store with one product; returns (sellerId, product).
    private async Task<(int sellerId, Product product)> SeedAsync(AppDbContext db)
    {
        var category = new Category { Name = "C", Slug = "c" };
        var seller = new User { FirstName = "Sel", LastName = "Ler", Email = "sel@s.com", PasswordHash = "h", Role = UserRole.Seller };
        db.Categories.Add(category);
        db.Users.Add(seller);
        await db.SaveChangesAsync();

        var store = new Store { Name = "Store", Slug = "store", Status = StoreStatus.Approved, OwnerUserId = seller.Id };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "Widget", SKU = "W-1", Price = 25m,
            CategoryId = category.Id, StoreId = store.Id,
            Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 }
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (seller.Id, product);
    }

    [Fact]
    public async Task RecordClick_ExistingProduct_PersistsClickEvent()
    {
        var db = CreateDb();
        var (_, product) = await SeedAsync(db);
        var service = CreateService(db);

        await service.RecordClickAsync(product.Id, userId: 42, CancellationToken.None);

        var interaction = await db.ProductInteractions.SingleAsync();
        interaction.Type.Should().Be(ProductInteractionType.Click);
        interaction.ProductId.Should().Be(product.Id);
        interaction.UserId.Should().Be(42);
        interaction.DurationMs.Should().BeNull();
    }

    [Fact]
    public async Task RecordClick_AnonymousVisitor_PersistsWithNullUser()
    {
        var db = CreateDb();
        var (_, product) = await SeedAsync(db);
        var service = CreateService(db);

        await service.RecordClickAsync(product.Id, userId: null, CancellationToken.None);

        var interaction = await db.ProductInteractions.SingleAsync();
        interaction.UserId.Should().BeNull();
    }

    [Fact]
    public async Task RecordClick_UnknownProduct_ThrowsNotFoundException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var act = () => service.RecordClickAsync(999, userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RecordTimeSpent_InvalidDuration_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, product) = await SeedAsync(db);
        var service = CreateService(db);

        var act = () => service.RecordTimeSpentAsync(
            product.Id, new RecordTimeSpentRequest { DurationMs = 0 }, userId: null, CancellationToken.None);

        await act.Should().ThrowAsync<DomainValidationException>();
        (await db.ProductInteractions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RecordTimeSpent_ValidDuration_PersistsTimeSpentEvent()
    {
        var db = CreateDb();
        var (_, product) = await SeedAsync(db);
        var service = CreateService(db);

        await service.RecordTimeSpentAsync(
            product.Id, new RecordTimeSpentRequest { DurationMs = 5000 }, userId: 7, CancellationToken.None);

        var interaction = await db.ProductInteractions.SingleAsync();
        interaction.Type.Should().Be(ProductInteractionType.TimeSpent);
        interaction.DurationMs.Should().Be(5000);
    }

    [Fact]
    public async Task GetClickStats_CountsClicksAndDistinctUsers()
    {
        var db = CreateDb();
        var (sellerId, product) = await SeedAsync(db);
        var service = CreateService(db);

        await service.RecordClickAsync(product.Id, userId: 1, CancellationToken.None);
        await service.RecordClickAsync(product.Id, userId: 1, CancellationToken.None);
        await service.RecordClickAsync(product.Id, userId: 2, CancellationToken.None);
        await service.RecordClickAsync(product.Id, userId: null, CancellationToken.None);

        var stats = await service.GetClickStatsAsync(product.Id, sellerId, isAdmin: false, CancellationToken.None);

        stats.TotalClicks.Should().Be(4);
        stats.UniqueUsers.Should().Be(2);
    }

    [Fact]
    public async Task GetTimeSpentStats_ComputesTotalAverageAndSampleCount()
    {
        var db = CreateDb();
        var (sellerId, product) = await SeedAsync(db);
        var service = CreateService(db);

        await service.RecordTimeSpentAsync(product.Id, new RecordTimeSpentRequest { DurationMs = 1000 }, null, CancellationToken.None);
        await service.RecordTimeSpentAsync(product.Id, new RecordTimeSpentRequest { DurationMs = 3000 }, null, CancellationToken.None);

        var stats = await service.GetTimeSpentStatsAsync(product.Id, sellerId, isAdmin: false, CancellationToken.None);

        stats.SampleCount.Should().Be(2);
        stats.TotalDurationMs.Should().Be(4000);
        stats.AverageDurationMs.Should().Be(2000);
    }

    [Fact]
    public async Task GetTimeSpentStats_NoSamples_ReturnsZeros()
    {
        var db = CreateDb();
        var (sellerId, product) = await SeedAsync(db);
        var service = CreateService(db);

        var stats = await service.GetTimeSpentStatsAsync(product.Id, sellerId, isAdmin: false, CancellationToken.None);

        stats.SampleCount.Should().Be(0);
        stats.TotalDurationMs.Should().Be(0);
        stats.AverageDurationMs.Should().Be(0);
    }

    [Fact]
    public async Task GetClickStats_NonOwnerSeller_ThrowsForbiddenException()
    {
        var db = CreateDb();
        var (_, product) = await SeedAsync(db);
        var service = CreateService(db);

        var act = () => service.GetClickStatsAsync(product.Id, userId: 9999, isAdmin: false, CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetClickStats_Admin_BypassesOwnershipCheck()
    {
        var db = CreateDb();
        var (_, product) = await SeedAsync(db);
        var service = CreateService(db);

        var stats = await service.GetClickStatsAsync(product.Id, userId: 9999, isAdmin: true, CancellationToken.None);

        stats.ProductId.Should().Be(product.Id);
    }

    [Fact]
    public async Task GetClickStats_UnknownProduct_ThrowsNotFoundException()
    {
        var db = CreateDb();
        var service = CreateService(db);

        var act = () => service.GetClickStatsAsync(999, userId: 1, isAdmin: true, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
