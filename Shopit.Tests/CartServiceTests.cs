using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class CartServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(AppDbContext db, Product product, User user)> SeedBasicData(AppDbContext db, int stock = 10)
    {
        var category = new Category { Name = "Test", Slug = "test" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Phone",
            SKU = "TEST-001",
            Price = 99.99m,
            CategoryId = category.Id,
            Inventory = new Inventory { Quantity = stock, LowStockThreshold = 2 }
        };
        db.Products.Add(product);

        var user = new User
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.com",
            PasswordHash = "hash",
            Role = UserRole.Customer
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (db, product, user);
    }

    [Fact]
    public async Task AddItemAsync_SufficientStock_AddsCartItem()
    {
        var db = CreateDb();
        var (_, product, user) = await SeedBasicData(db, stock: 10);
        var service = new CartService(db);

        var result = await service.AddItemAsync(user.Id, new AddCartItemRequest
        {
            ProductId = product.Id,
            Quantity = 2
        });

        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].ProductId.Should().Be(product.Id);
        result.Items[0].Quantity.Should().Be(2);
    }

    [Fact]
    public async Task AddItemAsync_InsufficientStock_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, product, user) = await SeedBasicData(db, stock: 1);
        var service = new CartService(db);

        var act = async () => await service.AddItemAsync(user.Id, new AddCartItemRequest
        {
            ProductId = product.Id,
            Quantity = 5
        });

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{product.Name}*");
    }

    [Fact]
    public async Task ApplyCouponAsync_ExpiredCoupon_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, _, user) = await SeedBasicData(db);

        var coupon = new Coupon
        {
            Code = "EXPIRED10",
            DiscountType = CouponDiscountType.Percent,
            DiscountValue = 10,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();

        var service = new CartService(db);

        var act = async () => await service.ApplyCouponAsync(user.Id, new ApplyCouponRequest
        {
            Code = "EXPIRED10"
        });

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task ApplyCouponAsync_ValidCoupon_SetsCouponId()
    {
        var db = CreateDb();
        var (_, _, user) = await SeedBasicData(db);

        var coupon = new Coupon
        {
            Code = "SAVE10",
            DiscountType = CouponDiscountType.Percent,
            DiscountValue = 10,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();

        var service = new CartService(db);

        var result = await service.ApplyCouponAsync(user.Id, new ApplyCouponRequest
        {
            Code = "SAVE10"
        });

        result.Should().NotBeNull();
        result.CouponCode.Should().Be("SAVE10");
        result.DiscountPercentage.Should().Be(10);
    }
}