using FluentAssertions;
using Moq;
using Shopit.Application.AI;
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

    private static IReviewModerationService CreateGenuineModerationService()
    {
        var mock = new Mock<IReviewModerationService>();
        mock.Setup(m => m.ModerateReviewAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviewModerationVerdict { IsSuspicious = false, Category = "genuine", Confidence = 0.95, Reason = "Looks like a normal review." });
        return mock.Object;
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

        var service = new ReviewService(db, CreateGenuineModerationService());

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

        var service = new ReviewService(db, CreateGenuineModerationService());

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

        var service = new ReviewService(db, CreateGenuineModerationService());

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

        var service = new ReviewService(db, CreateGenuineModerationService());

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

        var service = new ReviewService(db, CreateGenuineModerationService());

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

        var service = new ReviewService(db, CreateGenuineModerationService());

        var act = async () => await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = 99999, Rating = 4 }, buyer.Id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Review> CreateReviewAsync(AppDbContext db, Product product, User user, ReviewStatus status, int rating = 4, string? comment = "Test")
    {
        var review = new Review
        {
            ProductId = product.Id,
            UserId = user.Id,
            Rating = rating,
            Comment = comment,
            CreatedAt = DateTime.UtcNow,
            Status = status
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return review;
    }

    [Fact]
    public async Task GetByProductId_OnlyReturnsApprovedReviews()
    {
        var db = CreateDb();
        var (buyer, product, _) = await SeedAsync(db);
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Approved, comment: "Approved one");
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Pending, comment: "Pending one");
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Flagged, comment: "Flagged one");
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Rejected, comment: "Rejected one");

        var service = new ReviewService(db, CreateGenuineModerationService());

        var result = await service.GetByProductIdAsync(product.Id, new ReviewQueryParameters());

        result.TotalCount.Should().Be(1);
        result.Reviews.Should().ContainSingle(r => r.Comment == "Approved one");
    }

    [Fact]
    public async Task SubmitReview_CleanReview_BecomesApprovedAfterRulesAndAi()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);
        buyer.CreatedAt = DateTime.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();

        var service = new ReviewService(db, CreateGenuineModerationService());

        await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Comment = "Great" }, buyer.Id);

        var stored = await db.Reviews.FirstAsync(r => r.ProductId == product.Id && r.UserId == buyer.Id);
        stored.Status.Should().Be(ReviewStatus.Approved);
    }

    [Fact]
    public async Task ApproveReviewAsync_SetsStatusApprovedAndModeratedAt()
    {
        var db = CreateDb();
        var (buyer, product, _) = await SeedAsync(db);
        var review = await CreateReviewAsync(db, product, buyer, ReviewStatus.Flagged);

        var service = new ReviewService(db, CreateGenuineModerationService());

        var result = await service.ApproveReviewAsync(review.Id);

        result.Status.Should().Be(nameof(ReviewStatus.Approved));
        var stored = await db.Reviews.FirstAsync(r => r.Id == review.Id);
        stored.Status.Should().Be(ReviewStatus.Approved);
        stored.ModeratedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectReviewAsync_SetsStatusRejectedAndReason()
    {
        var db = CreateDb();
        var (buyer, product, _) = await SeedAsync(db);
        var review = await CreateReviewAsync(db, product, buyer, ReviewStatus.Flagged);

        var service = new ReviewService(db, CreateGenuineModerationService());

        var result = await service.RejectReviewAsync(review.Id, new RejectReviewRequest { Reason = "Spam" });

        result.Status.Should().Be(nameof(ReviewStatus.Rejected));
        var stored = await db.Reviews.FirstAsync(r => r.Id == review.Id);
        stored.Status.Should().Be(ReviewStatus.Rejected);
        stored.ModerationReason.Should().Be("Spam");
        stored.ModeratedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ApproveReviewAsync_ReviewNotFound_ThrowsNotFound()
    {
        var db = CreateDb();
        var service = new ReviewService(db, CreateGenuineModerationService());

        var act = async () => await service.ApproveReviewAsync(99999);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RejectReviewAsync_ReviewNotFound_ThrowsNotFound()
    {
        var db = CreateDb();
        var service = new ReviewService(db, CreateGenuineModerationService());

        var act = async () => await service.RejectReviewAsync(99999, new RejectReviewRequest { Reason = "x" });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetModerationQueueAsync_OnlyReturnsFlaggedReviews()
    {
        var db = CreateDb();
        var (buyer, product, _) = await SeedAsync(db);
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Flagged, comment: "Needs review");
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Approved, comment: "Fine");
        await CreateReviewAsync(db, product, buyer, ReviewStatus.Pending, comment: "Waiting");

        var service = new ReviewService(db, CreateGenuineModerationService());

        var result = await service.GetModerationQueueAsync(new ReviewQueryParameters());

        result.TotalCount.Should().Be(1);
        result.Reviews.Should().ContainSingle(r => r.Comment == "Needs review");
    }

    [Fact]
    public async Task SubmitReview_SelfReview_FlagsAutomaticallyWithReason()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        buyer.CreatedAt = DateTime.UtcNow.AddDays(-30);
        store.OwnerUserId = buyer.Id;
        await db.SaveChangesAsync();
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);

        var service = new ReviewService(db, CreateGenuineModerationService());

        await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Comment = "Great" }, buyer.Id);

        var stored = await db.Reviews.FirstAsync(r => r.ProductId == product.Id && r.UserId == buyer.Id);
        stored.Status.Should().Be(ReviewStatus.Flagged);
        stored.ModerationReason.Should().Contain("Self-review");
    }

    [Fact]
    public async Task SubmitReview_BurstOfReviewsOnProduct_FlagsAutomatically()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        buyer.CreatedAt = DateTime.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);

        // Simulate 5 other recent reviews on the same product to trip the burst threshold.
        for (int i = 0; i < 5; i++)
        {
            var otherUser = new User { FirstName = "U", LastName = i.ToString(), Email = $"burst{i}@s.com", PasswordHash = "h", Role = UserRole.Customer, CreatedAt = DateTime.UtcNow.AddDays(-30) };
            db.Users.Add(otherUser);
            await db.SaveChangesAsync();
            db.Reviews.Add(new Review { ProductId = product.Id, UserId = otherUser.Id, Rating = 4, CreatedAt = DateTime.UtcNow, Status = ReviewStatus.Pending });
        }
        await db.SaveChangesAsync();

        var service = new ReviewService(db, CreateGenuineModerationService());

        await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Comment = "Also great" }, buyer.Id);

        var stored = await db.Reviews.FirstAsync(r => r.ProductId == product.Id && r.UserId == buyer.Id);
        stored.Status.Should().Be(ReviewStatus.Flagged);
        stored.ModerationReason.Should().Contain("burst");
    }

    [Fact]
    public async Task SubmitReview_AiFlagsContent_SetsStatusFlaggedWithAiReason()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);
        buyer.CreatedAt = DateTime.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();

        var moderationMock = new Mock<IReviewModerationService>();
        moderationMock.Setup(m => m.ModerateReviewAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReviewModerationVerdict { IsSuspicious = true, Category = "toxic", Confidence = 0.9, Reason = "Contains offensive language." });

        var service = new ReviewService(db, moderationMock.Object);

        await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 1, Comment = "This is terrible" }, buyer.Id);

        var stored = await db.Reviews.FirstAsync(r => r.ProductId == product.Id && r.UserId == buyer.Id);
        stored.Status.Should().Be(ReviewStatus.Flagged);
        stored.ModerationReason.Should().Contain("toxic");
    }

    [Fact]
    public async Task SubmitReview_AiModerationThrows_FailsOpenToFlagged()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);
        await SeedStoreOrderAsync(db, buyer.Id, store, product, OrderStatus.Delivered);
        buyer.CreatedAt = DateTime.UtcNow.AddDays(-30);
        await db.SaveChangesAsync();

        var moderationMock = new Mock<IReviewModerationService>();
        moderationMock.Setup(m => m.ModerateReviewAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("The Gemini API is currently unavailable."));

        var service = new ReviewService(db, moderationMock.Object);

        await service.SubmitReviewAsync(
            new SubmitReviewRequest { ProductId = product.Id, Rating = 5, Comment = "Great product" }, buyer.Id);

        var stored = await db.Reviews.FirstAsync(r => r.ProductId == product.Id && r.UserId == buyer.Id);
        stored.Status.Should().Be(ReviewStatus.Flagged);
        stored.ModerationReason.Should().Contain("unavailable");
    }

    [Fact]
    public async Task GetModerationQueueAsync_FilterByStoreId_OnlyReturnsThatStoresReviews()
    {
        var db = CreateDb();
        var (buyer, product, store) = await SeedAsync(db);

        var otherSeller = new User { FirstName = "Other", LastName = "Seller", Email = "otherseller@s.com", PasswordHash = "h", Role = UserRole.Seller };
        db.Users.Add(otherSeller);
        await db.SaveChangesAsync();
        var otherStore = new Store { Name = "Other Store", Slug = "other-store", Status = StoreStatus.Approved, OwnerUserId = otherSeller.Id };
        db.Stores.Add(otherStore);
        await db.SaveChangesAsync();
        var otherProduct = new Product { Name = "Other Widget", SKU = "OW-1", Price = 10m, CategoryId = product.CategoryId, StoreId = otherStore.Id, Inventory = new Inventory { Quantity = 5, LowStockThreshold = 1 } };
        db.Products.Add(otherProduct);
        await db.SaveChangesAsync();

        await CreateReviewAsync(db, product, buyer, ReviewStatus.Flagged, comment: "From store one");
        db.Reviews.Add(new Review { ProductId = otherProduct.Id, UserId = buyer.Id, Rating = 3, Comment = "From store two", CreatedAt = DateTime.UtcNow, Status = ReviewStatus.Flagged });
        await db.SaveChangesAsync();

        var service = new ReviewService(db, CreateGenuineModerationService());

        var result = await service.GetModerationQueueAsync(new ReviewQueryParameters { StoreId = store.Id });

        result.TotalCount.Should().Be(1);
        result.Reviews.Should().ContainSingle(r => r.Comment == "From store one");
    }
}
