using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shopit.Application.DTOs.Stores;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class StoreServiceTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<int> SeedSellerAsync(AppDbContext db, string email = "seller@test.com")
    {
        var user = new User
        {
            FirstName = "Sal",
            LastName = "Seller",
            Email = email,
            PasswordHash = "hash",
            Role = UserRole.Seller
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task CreateStore_StartsPending_WithSlug()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var result = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "My Cool Store" });

        result.Status.Should().Be(StoreStatus.Pending.ToString());
        result.Slug.Should().Be("my-cool-store");
        result.OwnerUserId.Should().Be(sellerId);
    }

    [Fact]
    public async Task CreateStore_DuplicateName_GeneratesUniqueSlug()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var first = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "Repeat" });
        var second = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "Repeat" });

        first.Slug.Should().Be("repeat");
        second.Slug.Should().Be("repeat-2");
    }

    [Fact]
    public async Task OneSeller_CanOwnMultipleStores()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "Store A" });
        await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "Store B" });

        var mine = await service.GetMyStoresAsync(sellerId);
        mine.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPendingStores_ReturnsOnlyPending()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var a = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "A" });
        await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "B" });
        await service.ApproveStoreAsync(a.Id);

        var pending = await service.GetPendingStoresAsync();

        pending.Should().HaveCount(1);
        pending[0].Name.Should().Be("B");
    }

    [Fact]
    public async Task Approve_Reject_Suspend_FollowValidTransitions()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var s1 = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "S1" });
        var s2 = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "S2" });

        (await service.ApproveStoreAsync(s1.Id)).Status.Should().Be(StoreStatus.Approved.ToString());
        (await service.RejectStoreAsync(s2.Id)).Status.Should().Be(StoreStatus.Rejected.ToString());
        (await service.SuspendStoreAsync(s1.Id)).Status.Should().Be(StoreStatus.Suspended.ToString());
        // Suspended -> Approved reinstatement
        (await service.ApproveStoreAsync(s1.Id)).Status.Should().Be(StoreStatus.Approved.ToString());
    }

    [Fact]
    public async Task Suspend_PendingStore_ThrowsConflict()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var s = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "S" });

        var act = async () => await service.SuspendStoreAsync(s.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Reject_ApprovedStore_ThrowsConflict()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var s = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "S" });
        await service.ApproveStoreAsync(s.Id);

        var act = async () => await service.RejectStoreAsync(s.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Approve_UnknownStore_ThrowsNotFound()
    {
        using var db = CreateDb();
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var act = async () => await service.ApproveStoreAsync(999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetStoreBySlug_ApprovedStore_ReturnsIt()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var created = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "Visible Shop" });
        await service.ApproveStoreAsync(created.Id);

        var result = await service.GetStoreBySlugAsync("visible-shop");

        result.Slug.Should().Be("visible-shop");
        result.Status.Should().Be(StoreStatus.Approved.ToString());
    }

    [Fact]
    public async Task GetStoreBySlug_NonApprovedStore_ThrowsNotFound()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "Hidden Shop" }); // stays Pending

        var act = async () => await service.GetStoreBySlugAsync("hidden-shop");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetStoreBySlug_UnknownSlug_ThrowsNotFound()
    {
        using var db = CreateDb();
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var act = async () => await service.GetStoreBySlugAsync("does-not-exist");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetMyStores_ReturnsOnlyCallersStores()
    {
        using var db = CreateDb();
        var sellerA = await SeedSellerAsync(db, "a@test.com");
        var sellerB = await SeedSellerAsync(db, "b@test.com");
        var service = new StoreService(db, Mock.Of<ICacheService>());

        await service.CreateStoreAsync(sellerA, new CreateStoreRequest { Name = "A Store" });
        await service.CreateStoreAsync(sellerB, new CreateStoreRequest { Name = "B Store One" });
        await service.CreateStoreAsync(sellerB, new CreateStoreRequest { Name = "B Store Two" });

        var mine = await service.GetMyStoresAsync(sellerA);

        mine.Should().ContainSingle();
        mine[0].Name.Should().Be("A Store");
        mine[0].OwnerUserId.Should().Be(sellerA);
    }

    [Fact]
    public async Task StoresOfSameSeller_ModeratedIndependently()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new StoreService(db, Mock.Of<ICacheService>());

        var a = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "A" });
        var b = await service.CreateStoreAsync(sellerId, new CreateStoreRequest { Name = "B" });

        await service.ApproveStoreAsync(a.Id);

        var stores = await service.GetMyStoresAsync(sellerId);
        stores.Single(s => s.Id == a.Id).Status.Should().Be(StoreStatus.Approved.ToString());
        stores.Single(s => s.Id == b.Id).Status.Should().Be(StoreStatus.Pending.ToString());
    }
}
