using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Reviews;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class ReviewServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(User buyer, Product product, Store store)> SeedAsync(AppDbContext db)
    {
        var category = new Category { Name = "C", Slug = "c" };
        var seller = new User { FirstName = "Sel", LastName = "Ler", Email = "sel@s.com", PasswordHash = "h", Role = UserRole.Seller };
        var buyer = new User { FirstName = "Buy", LastName = "Er", Email = "buy@s.com", PasswordHash = "h", Role = UserRole.Customer };
        db.Categories.Add(category);
        db.Users.AddRange(seller, buyer);
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

        return (buyer, product, store);
    }

    // Attaches an Order + StoreOrder (with one StoreOrderItem for the product) at the given status.
    private async Task SeedStoreOrderAsync(AppDbContext db, int buyerId, Store store, Product product, OrderStatus status)
    {
        var order = new Order { UserId = buyerId, TotalAmount = product.Price, ShippingAddress = "1 St", CreatedAt = DateTime.UtcNow };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        db.StoreOrders.Add(new StoreOrder
        {
            OrderId = order.Id,
            StoreId = store.Id,
            Status = status,
            SubTotal = product.Price,
            SellerNetAmount = product.Price,
            StoreOrderItems =
            {
                new StoreOrderItem
                {
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    Quantity = 1,
                    UnitPrice = product.Price,
                    Subtotal = product.Price
                }
            }
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SubmitReview_DeliveredPurchase_CreatesReview()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);

        var service = new ReviewService(db);

        var result = await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Comment = "Great" }, buyer.Id);

        result.Should().NotBeNull();
        result.Rating.Should().Be(5);
        result.Comment.Should().Be("Great");

        (await db.Reviews.CountAsync(r => r.ProductId == product.Id && r.UserId == buyer.Id))
            .Should().Be(1);
    }

    [Fact]
    public async Task SubmitReview_NoPurchase_ThrowsForbidden()
    {
        var db = CreateDb();
        var (buyer, product, _) = await SeedAsync(db);

        var service = new ReviewService(db);

        var act = async () => await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 4 }, buyer.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Processing)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task SubmitReview_PurchaseNotDelivered_ThrowsForbidden(OrderStatus status)
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, status);

        var service = new ReviewService(db);

        var act = async () => await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 4 }, buyer.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SubmitReview_AnotherUsersDeliveredPurchase_ThrowsForbidden()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);

        // A different customer who never bought the product.
        var otherUser = new User { FirstName = "Oth", LastName = "Er", Email = "oth@s.com", PasswordHash = "h", Role = UserRole.Customer };
        db.Users.Add(otherUser);
        await db.SaveChangesAsync();

        var service = new ReviewService(db);

        var act = async () => await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 4 }, otherUser.Id);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task SubmitReview_AlreadyReviewed_ThrowsConflict()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);

        var service = new ReviewService(db);

        await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 5 }, buyer.Id);

        var act = async () => await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 3 }, buyer.Id);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task SubmitReview_ProductNotFound_ThrowsNotFound()
    {
        var db = CreateDb();
        var (buyer, _, _) = await SeedAsync(db);

        var service = new ReviewService(db);

        var act = async () => await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = 99999, Rating = 4 }, buyer.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
