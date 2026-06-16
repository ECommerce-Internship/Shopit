using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class OrderServiceTests
{
    private AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private IEmailService CreateEmailServiceStub()
    {
        var mock = new Mock<IEmailService>();
        mock.Setup(e => e.SendOrderConfirmationAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        return mock.Object;
    }

    private IServiceScopeFactory CreateScopeFactoryStub(IEmailService emailService)
    {
        var scopeMock = new Mock<IServiceScope>();
        var providerMock = new Mock<IServiceProvider>();
        providerMock.Setup(p => p.GetService(typeof(IEmailService))).Returns(emailService);
        scopeMock.Setup(s => s.ServiceProvider).Returns(providerMock.Object);

        var factoryMock = new Mock<IServiceScopeFactory>();
        factoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return factoryMock.Object;
    }

    private async Task<(AppDbContext db, User user, Product product1, Product product2)> SeedData(AppDbContext db)
    {
        var category = new Category { Name = "Test", Slug = "test" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product1 = new Product
        {
            Name = "Phone",
            SKU = "PHONE-001",
            Price = 699.99m,
            CategoryId = category.Id,
            Inventory = new Inventory { Quantity = 10, LowStockThreshold = 2, RowVersion = new byte[8] }
        };

        var product2 = new Product
        {
            Name = "T-Shirt",
            SKU = "SHIRT-001",
            Price = 29.99m,
            CategoryId = category.Id,
            Inventory = new Inventory { Quantity = 5, LowStockThreshold = 1, RowVersion = new byte[8] }
        };

        db.Products.AddRange(product1, product2);

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

        return (db, user, product1, product2);
    }

    private async Task<Cart> CreateCartWithItems(AppDbContext db, int userId, List<(int productId, int quantity)> items)
    {
        var cart = new Cart { UserId = userId, Status = CartStatus.Active };
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        foreach (var (productId, quantity) in items)
        {
            db.CartItems.Add(new CartItem
            {
                CartId = cart.Id,
                ProductId = productId,
                Quantity = quantity
            });
        }
        await db.SaveChangesAsync();

        return cart;
    }

    [Fact]
    public async Task PlaceOrderAsync_ValidCart_CreatesOrderAndDeductsInventory()
    {
        var db = CreateDb();
        var (_, user, product1, product2) = await SeedData(db);

        await CreateCartWithItems(db, user.Id, new List<(int, int)>
        {
            (product1.Id, 2),
            (product2.Id, 1)
        });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var result = await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest
        {
            ShippingAddress = "123 Test St"
        });

        result.Should().NotBeNull();
        result.Status.Should().Be("Pending");
        result.Items.Should().HaveCount(2);
        result.TotalAmount.Should().Be((699.99m * 2) + (29.99m * 1));

        var updatedProduct1 = await db.Inventories.FirstAsync(i => i.ProductId == product1.Id);
        updatedProduct1.Quantity.Should().Be(8);

        var updatedProduct2 = await db.Inventories.FirstAsync(i => i.ProductId == product2.Id);
        updatedProduct2.Quantity.Should().Be(4);

        var cartItems = await db.CartItems.ToListAsync();
        cartItems.Should().BeEmpty();
    }

    [Fact]
    public async Task PlaceOrderAsync_EmptyCart_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, user, _, _) = await SeedData(db);

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var act = async () => await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest
        {
            ShippingAddress = "123 Test St"
        });

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task PlaceOrderAsync_ItemOutOfStock_ThrowsValidationExceptionWithProductName()
    {
        var db = CreateDb();
        var (_, user, product1, _) = await SeedData(db);

        await CreateCartWithItems(db, user.Id, new List<(int, int)>
        {
            (product1.Id, 100)
        });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var act = async () => await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest
        {
            ShippingAddress = "123 Test St"
        });

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{product1.Name}*");
    }

    [Fact]
    public async Task CancelOrderAsync_PendingStatus_SetsStatusCancelled()
    {
        var db = CreateDb();
        var (_, user, product1, _) = await SeedData(db);

        await CreateCartWithItems(db, user.Id, new List<(int, int)>
        {
            (product1.Id, 1)
        });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var order = await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest
        {
            ShippingAddress = "123 Test St"
        });

        var result = await service.CancelOrderAsync(order.Id, user.Id);

        result.Should().NotBeNull();
        result.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task CancelOrderAsync_NonPendingStatus_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, user, _, _) = await SeedData(db);

        var order = new Order
        {
            UserId = user.Id,
            Status = OrderStatus.Processing,
            TotalAmount = 699.99m,
            ShippingAddress = "123 Test St"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var act = async () => await service.CancelOrderAsync(order.Id, user.Id);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Processing*");
    }
}