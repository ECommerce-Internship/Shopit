using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class DashboardServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    // Mock.Of<ICacheService>() returns default for GetAsync (cache miss) and a no-op SetAsync,
    // so every call recomputes from the in-memory DB.
    private DashboardService NewService(AppDbContext db) =>
        new(db, Mock.Of<ICacheService>());

    // Two sellers, two stores. Seller A has an active store order (gross 200) and a CANCELLED one
    // (gross 50, must be excluded). Seller B has an active store order (gross 90).
    private async Task<(User sellerA, User sellerB)> SeedAsync(AppDbContext db)
    {
        var category = new Category { Name = "C", Slug = "c" };
        var sellerA = new User { FirstName = "A", LastName = "A", Email = "a@s.com", PasswordHash = "h", Role = UserRole.Seller };
        var sellerB = new User { FirstName = "B", LastName = "B", Email = "b@s.com", PasswordHash = "h", Role = UserRole.Seller };
        var buyer = new User { FirstName = "Buy", LastName = "Er", Email = "buy@s.com", PasswordHash = "h", Role = UserRole.Customer };
        db.Categories.Add(category);
        db.Users.AddRange(sellerA, sellerB, buyer);
        await db.SaveChangesAsync();

        var storeA = new Store { Name = "Store A", Slug = "store-a", Status = StoreStatus.Approved, CommissionRate = 0.10m, OwnerUserId = sellerA.Id };
        var storeB = new Store { Name = "Store B", Slug = "store-b", Status = StoreStatus.Approved, CommissionRate = 0.20m, OwnerUserId = sellerB.Id };
        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        // pA1: not low; pA2: low (1 <= 2); pB1: not low
        var pA1 = new Product { Name = "A-One", SKU = "A-1", Price = 100m, CategoryId = category.Id, StoreId = storeA.Id, Inventory = new Inventory { Quantity = 8, LowStockThreshold = 2 } };
        var pA2 = new Product { Name = "A-Two", SKU = "A-2", Price = 50m, CategoryId = category.Id, StoreId = storeA.Id, Inventory = new Inventory { Quantity = 1, LowStockThreshold = 2 } };
        var pB1 = new Product { Name = "B-One", SKU = "B-1", Price = 30m, CategoryId = category.Id, StoreId = storeB.Id, Inventory = new Inventory { Quantity = 5, LowStockThreshold = 1 } };
        db.Products.AddRange(pA1, pA2, pB1);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;

        // order1 — active store orders for A (200) and B (90)
        var order1 = new Order { UserId = buyer.Id, TotalAmount = 290m, ShippingAddress = "1 St", CreatedAt = now };
        // order2 — a CANCELLED store order for A (50), must not count toward revenue/commission/top-products
        var order2 = new Order { UserId = buyer.Id, TotalAmount = 50m, ShippingAddress = "1 St", CreatedAt = now };
        db.Orders.AddRange(order1, order2);
        await db.SaveChangesAsync();

        db.StoreOrders.AddRange(
            new StoreOrder
            {
                OrderId = order1.Id, StoreId = storeA.Id, Status = OrderStatus.Processing,
                SubTotal = 200m, CommissionAmount = 20m, SellerNetAmount = 180m,
                StoreOrderItems = { new StoreOrderItem { ProductId = pA1.Id, ProductNameSnapshot = "A-One", Quantity = 2, UnitPrice = 100m, Subtotal = 200m } }
            },
            new StoreOrder
            {
                OrderId = order1.Id, StoreId = storeB.Id, Status = OrderStatus.Processing,
                SubTotal = 90m, CommissionAmount = 18m, SellerNetAmount = 72m,
                StoreOrderItems = { new StoreOrderItem { ProductId = pB1.Id, ProductNameSnapshot = "B-One", Quantity = 3, UnitPrice = 30m, Subtotal = 90m } }
            },
            new StoreOrder
            {
                OrderId = order2.Id, StoreId = storeA.Id, Status = OrderStatus.Cancelled,
                SubTotal = 50m, CommissionAmount = 5m, SellerNetAmount = 45m,
                StoreOrderItems = { new StoreOrderItem { ProductId = pA2.Id, ProductNameSnapshot = "A-Two", Quantity = 1, UnitPrice = 50m, Subtotal = 50m } }
            });
        await db.SaveChangesAsync();

        return (sellerA, sellerB);
    }

    [Fact]
    public async Task GetSellerSummary_ReturnsOnlyCallersMetrics()
    {
        var db = CreateDb();
        var (sellerA, _) = await SeedAsync(db);

        var result = await NewService(db).GetSellerSummaryAsync(sellerA.Id);

        result.GrossSales.Should().Be(200m);        // excludes seller B (90) and cancelled A (50)
        result.TotalCommission.Should().Be(20m);
        result.NetEarnings.Should().Be(180m);
        result.TotalOrders.Should().Be(2);          // both of seller A's store orders, any status
        result.LowStockCount.Should().Be(1);        // only pA2 is at/under threshold
        result.TodaysNewOrders.Should().Be(2);      // both orders created today
    }

    [Fact]
    public async Task GetSellerSummary_ExcludesCancelledStoreOrders()
    {
        var db = CreateDb();
        var (sellerA, _) = await SeedAsync(db);

        var result = await NewService(db).GetSellerSummaryAsync(sellerA.Id);

        // Cancelled order (gross 50 / commission 5 / net 45) must not be in the money totals.
        result.GrossSales.Should().Be(200m);
        result.TotalCommission.Should().Be(20m);
        result.NetEarnings.Should().Be(180m);
    }

    [Fact]
    public async Task GetSellerTopProducts_ScopedToOwnStoresAndExcludesCancelled()
    {
        var db = CreateDb();
        var (sellerA, _) = await SeedAsync(db);

        var result = (await NewService(db).GetSellerTopProductsAsync(sellerA.Id)).ToList();

        result.Should().ContainSingle();            // only pA1; pB1 (other seller) and pA2 (cancelled) excluded
        result[0].ProductName.Should().Be("A-One");
        result[0].UnitsSold.Should().Be(2);
        result[0].Revenue.Should().Be(200m);
    }

    [Fact]
    public async Task GetSellerOrdersByStatus_ScopedToOwnStores()
    {
        var db = CreateDb();
        var (sellerA, _) = await SeedAsync(db);

        var result = (await NewService(db).GetSellerOrdersByStatusAsync(sellerA.Id)).ToList();

        result.Should().HaveCount(2);
        result.Single(r => r.Status == "Processing").Count.Should().Be(1);
        result.Single(r => r.Status == "Cancelled").Count.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_Admin_IncludesTotalCommissionExcludingCancelled()
    {
        var db = CreateDb();
        await SeedAsync(db);

        var result = await NewService(db).GetSummaryAsync();

        // 20 (store A active) + 18 (store B active); the cancelled store order's 5 is excluded.
        result.TotalCommission.Should().Be(38m);
    }
}
