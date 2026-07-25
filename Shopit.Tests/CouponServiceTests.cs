using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Coupons;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class CouponServiceTests
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

    private static async Task<int> SeedStoreAsync(AppDbContext db, int ownerUserId, string name = "My Store")
    {
        var store = new Store
        {
            Name = name,
            Slug = name.ToLowerInvariant().Replace(' ', '-'),
            Status = StoreStatus.Approved,
            OwnerUserId = ownerUserId
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        return store.Id;
    }

    private static CreateCouponRequest ValidRequest(int? storeId) => new()
    {
        Code = "SAVE10",
        DiscountType = CouponDiscountType.Percent,
        DiscountValue = 10,
        StoreId = storeId
    };

    // ----- Ownership -----

    [Fact]
    public async Task CreateCoupon_SellerOwnsStore_CreatesScopedCoupon()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var storeId = await SeedStoreAsync(db, sellerId);
        var service = new CouponService(db);

        var result = await service.CreateAsync(sellerId, isAdmin: false, ValidRequest(storeId));

        result.Code.Should().Be("SAVE10");
        result.StoreId.Should().Be(storeId);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateCoupon_SellerTargetsAnotherStore_ThrowsForbiddenException()
    {
        using var db = CreateDb();
        var ownerId = await SeedSellerAsync(db, "owner@test.com");
        var otherSellerId = await SeedSellerAsync(db, "other@test.com");
        var storeId = await SeedStoreAsync(db, ownerId);
        var service = new CouponService(db);

        var act = async () => await service.CreateAsync(otherSellerId, isAdmin: false, ValidRequest(storeId));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateCoupon_SellerWithoutStoreId_ThrowsForbiddenException()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db);
        var service = new CouponService(db);

        var act = async () => await service.CreateAsync(sellerId, isAdmin: false, ValidRequest(storeId: null));

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task CreateCoupon_AdminWithNullStore_CreatesGlobalCoupon()
    {
        using var db = CreateDb();
        var service = new CouponService(db);

        var result = await service.CreateAsync(userId: 999, isAdmin: true, ValidRequest(storeId: null));

        result.StoreId.Should().BeNull();
    }

    [Fact]
    public async Task UpdateCoupon_SellerEditsAnotherStoresCoupon_ThrowsForbiddenException()
    {
        using var db = CreateDb();
        var ownerId = await SeedSellerAsync(db, "owner@test.com");
        var otherSellerId = await SeedSellerAsync(db, "other@test.com");
        var storeId = await SeedStoreAsync(db, ownerId);
        var service = new CouponService(db);
        var coupon = await service.CreateAsync(ownerId, isAdmin: false, ValidRequest(storeId));

        var act = async () => await service.UpdateAsync(
            coupon.Id, otherSellerId, isAdmin: false,
            new UpdateCouponRequest { DiscountType = CouponDiscountType.Percent, DiscountValue = 20 });

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task GetAll_Seller_ReturnsOnlyOwnStoreCoupons()
    {
        using var db = CreateDb();
        var sellerId = await SeedSellerAsync(db, "owner@test.com");
        var otherId = await SeedSellerAsync(db, "other@test.com");
        var myStore = await SeedStoreAsync(db, sellerId, "Mine");
        var otherStore = await SeedStoreAsync(db, otherId, "Theirs");
        var service = new CouponService(db);
        await service.CreateAsync(sellerId, false, new CreateCouponRequest { Code = "MINE", DiscountType = CouponDiscountType.Percent, DiscountValue = 5, StoreId = myStore });
        await service.CreateAsync(otherId, false, new CreateCouponRequest { Code = "THEIRS", DiscountType = CouponDiscountType.Percent, DiscountValue = 5, StoreId = otherStore });
        await service.CreateAsync(999, true, new CreateCouponRequest { Code = "GLOBAL", DiscountType = CouponDiscountType.Percent, DiscountValue = 5, StoreId = null });

        var result = await service.GetAllAsync(sellerId, isAdmin: false);

        result.Should().ContainSingle();
        result[0].Code.Should().Be("MINE");
    }

    // ----- Duplicate code -----

    [Fact]
    public async Task CreateCoupon_DuplicateCode_ThrowsConflictException()
    {
        using var db = CreateDb();
        var service = new CouponService(db);
        await service.CreateAsync(1, isAdmin: true, ValidRequest(storeId: null));

        var act = async () => await service.CreateAsync(1, isAdmin: true, ValidRequest(storeId: null));

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*SAVE10*");
    }

    [Fact]
    public async Task CreateCoupon_DuplicateCodeDifferentCasing_ThrowsConflictException()
    {
        using var db = CreateDb();
        var service = new CouponService(db);
        await service.CreateAsync(1, isAdmin: true, ValidRequest(storeId: null));

        var act = async () => await service.CreateAsync(1, isAdmin: true,
            ValidRequest(storeId: null) with { Code = "save10" });

        await act.Should().ThrowAsync<ConflictException>();
    }

    // ----- Not found -----

    [Fact]
    public async Task GetById_UnknownId_ThrowsNotFoundException()
    {
        using var db = CreateDb();
        var service = new CouponService(db);

        var act = async () => await service.GetByIdAsync(999, userId: 1, isAdmin: true);

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*999*");
    }

    // ----- Usage-limit vs usage-count validation -----

    [Fact]
    public async Task UpdateCoupon_UsageLimitBelowUsageCount_ThrowsValidationException()
    {
        using var db = CreateDb();
        var service = new CouponService(db);
        var created = await service.CreateAsync(1, isAdmin: true, ValidRequest(storeId: null));

        // Simulate prior redemptions.
        var entity = await db.Coupons.FirstAsync(c => c.Id == created.Id);
        entity.UsageCount = 5;
        await db.SaveChangesAsync();

        var act = async () => await service.UpdateAsync(
            created.Id, userId: 1, isAdmin: true,
            new UpdateCouponRequest { DiscountType = CouponDiscountType.Percent, DiscountValue = 10, UsageLimit = 3 });

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ----- Deactivate -----

    [Fact]
    public async Task Deactivate_ExistingCoupon_SetsInactive()
    {
        using var db = CreateDb();
        var service = new CouponService(db);
        var created = await service.CreateAsync(1, isAdmin: true, ValidRequest(storeId: null));

        var result = await service.DeactivateAsync(created.Id, userId: 1, isAdmin: true);

        result.IsActive.Should().BeFalse();
    }
}
