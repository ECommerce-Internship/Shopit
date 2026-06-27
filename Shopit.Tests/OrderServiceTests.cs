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

    private async Task<(AppDbContext db, User user, Product product1, Product product2, Store store)> SeedData(AppDbContext db)
    {
        var category = new Category { Name = "Test", Slug = "test" };
        db.Categories.Add(category);

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

        var store = new Store
        {
            Name = "Test Store",
            Slug = "test-store",
            Status = StoreStatus.Approved,
            CommissionRate = 0,
            OwnerUserId = user.Id
        };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var product1 = new Product
        {
            Name = "Phone",
            SKU = "PHONE-001",
            Price = 699.99m,
            CategoryId = category.Id,
            StoreId = store.Id,
            Inventory = new Inventory { Quantity = 10, LowStockThreshold = 2 }
        };

        var product2 = new Product
        {
            Name = "T-Shirt",
            SKU = "SHIRT-001",
            Price = 29.99m,
            CategoryId = category.Id,
            StoreId = store.Id,
            Inventory = new Inventory { Quantity = 5, LowStockThreshold = 1 }
        };

        db.Products.AddRange(product1, product2);
        await db.SaveChangesAsync();

        return (db, user, product1, product2, store);
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
        var (_, user, product1, product2, _) = await SeedData(db);

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
        result.Status.Should().Be("Processing");
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
    public async Task PlaceOrderAsync_ValidCart_CreatesSingleStoreOrderForProductStore()
    {
        var db = CreateDb();
        var (_, user, product1, product2, store) = await SeedData(db);

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

        var storeOrders = await db.StoreOrders
            .Where(so => so.OrderId == result.Id)
            .ToListAsync();

        storeOrders.Should().HaveCount(1);
        storeOrders[0].StoreId.Should().Be(store.Id);
        storeOrders[0].Status.Should().Be(OrderStatus.Processing);
        storeOrders[0].SubTotal.Should().Be((699.99m * 2) + (29.99m * 1));

        var storeOrderItems = await db.StoreOrderItems
            .Where(soi => soi.StoreOrderId == storeOrders[0].Id)
            .ToListAsync();

        storeOrderItems.Should().HaveCount(2);
    }

    [Fact]
    public async Task PlaceOrderAsync_ValidCart_StoreOrderItemsCaptureSnapshot()
    {
        var db = CreateDb();
        var (_, user, product1, _, _) = await SeedData(db);

        await CreateCartWithItems(db, user.Id, new List<(int, int)>
        {
            (product1.Id, 3)
        });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var result = await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest
        {
            ShippingAddress = "123 Test St"
        });

        var item = await db.StoreOrderItems.SingleAsync();
        item.ProductId.Should().Be(product1.Id);
        item.ProductNameSnapshot.Should().Be(product1.Name);
        item.UnitPrice.Should().Be(product1.Price);
        item.Quantity.Should().Be(3);
        item.Subtotal.Should().Be(product1.Price * 3);
    }

    [Fact]
    public async Task PlaceOrderAsync_EmptyCart_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, user, _, _, _) = await SeedData(db);

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
        var (_, user, product1, _, _) = await SeedData(db);

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
        var (_, user, product1, _, store) = await SeedData(db);

        // Checkout now produces Processing orders, so build a Pending order directly to exercise cancel.
        var order = new Order { UserId = user.Id, TotalAmount = product1.Price, ShippingAddress = "123 Test St" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        db.StoreOrders.Add(new StoreOrder
        {
            OrderId = order.Id,
            StoreId = store.Id,
            Status = OrderStatus.Pending,
            SubTotal = product1.Price,
            SellerNetAmount = product1.Price,
            StoreOrderItems =
            {
                new StoreOrderItem
                {
                    ProductId = product1.Id,
                    ProductNameSnapshot = product1.Name,
                    Quantity = 1,
                    UnitPrice = product1.Price,
                    Subtotal = product1.Price
                }
            }
        });
        await db.SaveChangesAsync();

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var result = await service.CancelOrderAsync(order.Id, user.Id);

        result.Should().NotBeNull();
        result.Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task CancelOrderAsync_NonPendingStatus_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, user, _, _, store) = await SeedData(db);

        var order = new Order
        {
            UserId = user.Id,
            TotalAmount = 699.99m,
            ShippingAddress = "123 Test St"
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        db.StoreOrders.Add(new StoreOrder
        {
            OrderId = order.Id,
            StoreId = store.Id,
            Status = OrderStatus.Processing,
            SubTotal = 699.99m,
            SellerNetAmount = 699.99m
        });
        await db.SaveChangesAsync();

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var act = async () => await service.CancelOrderAsync(order.Id, user.Id);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Processing*");
    }

    [Fact]
    public async Task PlaceOrderAsync_ProductFromNonApprovedStore_ThrowsValidationException()
    {
        var db = CreateDb();
        var (_, user, product1, _, _) = await SeedData(db);

        // A product owned by a store that is not yet Approved cannot be sold (SCRUM-132).
        var pendingStore = new Store
        {
            Name = "Pending Store",
            Slug = "pending-store",
            Status = StoreStatus.Pending,
            CommissionRate = 0,
            OwnerUserId = user.Id
        };
        db.Stores.Add(pendingStore);
        await db.SaveChangesAsync();

        var pendingProduct = new Product
        {
            Name = "Unapproved Widget",
            SKU = "PEND-001",
            Price = 49.99m,
            CategoryId = product1.CategoryId,
            StoreId = pendingStore.Id,
            Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 }
        };
        db.Products.Add(pendingProduct);
        await db.SaveChangesAsync();

        await CreateCartWithItems(db, user.Id, new List<(int, int)> { (pendingProduct.Id, 1) });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var act = async () => await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest
        {
            ShippingAddress = "123 Test St"
        });

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*not currently available*");
    }

    [Fact]
    public async Task PlaceOrderAsync_MultiStoreCart_FansOutPerStoreWithCommission()
    {
        var db = CreateDb();

        var category = new Category { Name = "Test", Slug = "test" };
        db.Categories.Add(category);
        var user = new User { FirstName = "B", LastName = "Uyer", Email = "buyer@test.com", PasswordHash = "h", Role = UserRole.Customer };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var storeA = new Store { Name = "Store A", Slug = "store-a", Status = StoreStatus.Approved, CommissionRate = 0.10m, OwnerUserId = user.Id };
        var storeB = new Store { Name = "Store B", Slug = "store-b", Status = StoreStatus.Approved, CommissionRate = 0.20m, OwnerUserId = user.Id };
        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        var pA = new Product { Name = "A-Widget", SKU = "A-1", Price = 100m, CategoryId = category.Id, StoreId = storeA.Id, Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 } };
        var pB = new Product { Name = "B-Gadget", SKU = "B-1", Price = 50m, CategoryId = category.Id, StoreId = storeB.Id, Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 } };
        db.Products.AddRange(pA, pB);
        await db.SaveChangesAsync();

        await CreateCartWithItems(db, user.Id, new List<(int, int)> { (pA.Id, 2), (pB.Id, 3) });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var result = await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest { ShippingAddress = "1 St" });

        // 1 Order, 2 StoreOrders
        (await db.Orders.CountAsync()).Should().Be(1);
        var storeOrders = await db.StoreOrders.Where(so => so.OrderId == result.Id).ToListAsync();
        storeOrders.Should().HaveCount(2);

        var soA = storeOrders.Single(s => s.StoreId == storeA.Id);
        var soB = storeOrders.Single(s => s.StoreId == storeB.Id);

        soA.SubTotal.Should().Be(200m);          // 100 * 2
        soB.SubTotal.Should().Be(150m);          // 50 * 3
        soA.CommissionAmount.Should().Be(20m);   // 200 * 0.10
        soB.CommissionAmount.Should().Be(30m);   // 150 * 0.20
        soA.SellerNetAmount.Should().Be(180m);
        soB.SellerNetAmount.Should().Be(120m);

        // per-store inventory deducted
        (await db.Inventories.FirstAsync(i => i.ProductId == pA.Id)).Quantity.Should().Be(8);
        (await db.Inventories.FirstAsync(i => i.ProductId == pB.Id)).Quantity.Should().Be(7);

        // exactly one Payment on the parent
        var payments = await db.Payments.Where(p => p.OrderId == result.Id).ToListAsync();
        payments.Should().HaveCount(1);
        payments[0].Status.Should().Be(PaymentStatus.Paid);
        payments[0].Amount.Should().Be(350m);    // 200 + 150, no discount

        // response carries the per-store breakdown
        result.StoreOrders.Should().HaveCount(2);
        result.StoreOrders.Select(s => s.StoreName).Should().Contain(new[] { "Store A", "Store B" });
    }

    [Fact]
    public async Task PlaceOrderAsync_CommissionRoundsHalfAwayFromZero()
    {
        var db = CreateDb();

        var category = new Category { Name = "Test", Slug = "test" };
        db.Categories.Add(category);
        var user = new User { FirstName = "B", LastName = "Uyer", Email = "buyer@test.com", PasswordHash = "h", Role = UserRole.Customer };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // 10.10 * 0.05 = 0.505 -> AwayFromZero rounds to 0.51 (banker's rounding would give 0.50).
        var store = new Store { Name = "Store", Slug = "store", Status = StoreStatus.Approved, CommissionRate = 0.05m, OwnerUserId = user.Id };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        var product = new Product { Name = "Odd", SKU = "ODD-1", Price = 10.10m, CategoryId = category.Id, StoreId = store.Id, Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 } };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        await CreateCartWithItems(db, user.Id, new List<(int, int)> { (product.Id, 1) });

        var emailService = CreateEmailServiceStub();
        var service = new OrderService(db, emailService, CreateScopeFactoryStub(emailService));

        var result = await service.PlaceOrderAsync(user.Id, new PlaceOrderRequest { ShippingAddress = "1 St" });

        var storeOrder = await db.StoreOrders.SingleAsync(so => so.OrderId == result.Id);
        storeOrder.SubTotal.Should().Be(10.10m);
        storeOrder.CommissionAmount.Should().Be(0.51m);   // 0.505 rounded away from zero
        storeOrder.SellerNetAmount.Should().Be(9.59m);    // 10.10 - 0.51
    }

    // ---- SCRUM-136: per-store fulfillment ----

    private OrderService NewService(AppDbContext db)
    {
        var es = CreateEmailServiceStub();
        return new OrderService(db, es, CreateScopeFactoryStub(es));
    }

    // One order with two StoreOrders owned by different sellers (both Processing).
    private async Task<(User sellerA, User sellerB, User buyer, Store storeA, StoreOrder soA, StoreOrder soB, Product pA, Product pB, Order order)> SeedMultiStoreOrder(AppDbContext db)
    {
        var category = new Category { Name = "C", Slug = "c" };
        var sellerA = new User { FirstName = "A", LastName = "A", Email = "a@s.com", PasswordHash = "h", Role = UserRole.Seller };
        var sellerB = new User { FirstName = "B", LastName = "B", Email = "b@s.com", PasswordHash = "h", Role = UserRole.Seller };
        var buyer = new User { FirstName = "Buy", LastName = "Er", Email = "buy@s.com", PasswordHash = "h", Role = UserRole.Customer };
        db.Categories.Add(category);
        db.Users.AddRange(sellerA, sellerB, buyer);
        await db.SaveChangesAsync();

        var storeA = new Store { Name = "Store A", Slug = "store-a", Status = StoreStatus.Approved, OwnerUserId = sellerA.Id };
        var storeB = new Store { Name = "Store B", Slug = "store-b", Status = StoreStatus.Approved, OwnerUserId = sellerB.Id };
        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        var pA = new Product { Name = "PA", SKU = "PA-1", Price = 10m, CategoryId = category.Id, StoreId = storeA.Id, Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 } };
        var pB = new Product { Name = "PB", SKU = "PB-1", Price = 20m, CategoryId = category.Id, StoreId = storeB.Id, Inventory = new Inventory { Quantity = 10, LowStockThreshold = 1 } };
        db.Products.AddRange(pA, pB);
        await db.SaveChangesAsync();

        var order = new Order { UserId = buyer.Id, TotalAmount = 30m, ShippingAddress = "1 St" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var soA = new StoreOrder { OrderId = order.Id, StoreId = storeA.Id, Status = OrderStatus.Processing, SubTotal = 20m, SellerNetAmount = 20m,
            StoreOrderItems = { new StoreOrderItem { ProductId = pA.Id, ProductNameSnapshot = "PA", Quantity = 2, UnitPrice = 10m, Subtotal = 20m } } };
        var soB = new StoreOrder { OrderId = order.Id, StoreId = storeB.Id, Status = OrderStatus.Processing, SubTotal = 60m, SellerNetAmount = 60m,
            StoreOrderItems = { new StoreOrderItem { ProductId = pB.Id, ProductNameSnapshot = "PB", Quantity = 3, UnitPrice = 20m, Subtotal = 60m } } };
        db.StoreOrders.AddRange(soA, soB);
        await db.SaveChangesAsync();

        return (sellerA, sellerB, buyer, storeA, soA, soB, pA, pB, order);
    }

    [Fact]
    public async Task UpdateStoreOrderStatus_SellerAdvancesOwn_OnlyTheirsChanges()
    {
        var db = CreateDb();
        var (sellerA, _, _, _, soA, soB, _, _, _) = await SeedMultiStoreOrder(db);

        var result = await NewService(db).UpdateStoreOrderStatusAsync(soA.Id,
            new UpdateOrderStatusRequest { Status = "Shipped" }, sellerA.Id, isAdmin: false);

        result.Status.Should().Be("Shipped");
        (await db.StoreOrders.FindAsync(soA.Id))!.Status.Should().Be(OrderStatus.Shipped);
        (await db.StoreOrders.FindAsync(soB.Id))!.Status.Should().Be(OrderStatus.Processing); // seller B untouched
    }

    [Fact]
    public async Task UpdateStoreOrderStatus_NonOwner_ThrowsForbidden()
    {
        var db = CreateDb();
        var (_, sellerB, _, _, soA, _, _, _, _) = await SeedMultiStoreOrder(db);

        var act = async () => await NewService(db).UpdateStoreOrderStatusAsync(soA.Id,
            new UpdateOrderStatusRequest { Status = "Shipped" }, sellerB.Id, isAdmin: false);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task UpdateStoreOrderStatus_Admin_CanUpdateAny()
    {
        var db = CreateDb();
        var (_, _, _, _, soA, _, _, _, _) = await SeedMultiStoreOrder(db);

        var result = await NewService(db).UpdateStoreOrderStatusAsync(soA.Id,
            new UpdateOrderStatusRequest { Status = "Shipped" }, userId: 0, isAdmin: true);

        result.Status.Should().Be("Shipped");
    }

    [Fact]
    public async Task UpdateStoreOrderStatus_InvalidTransition_Throws()
    {
        var db = CreateDb();
        var (sellerA, _, _, _, soA, _, _, _, _) = await SeedMultiStoreOrder(db);

        // Processing -> Delivered is not allowed (Processing -> Shipped only)
        var act = async () => await NewService(db).UpdateStoreOrderStatusAsync(soA.Id,
            new UpdateOrderStatusRequest { Status = "Delivered" }, sellerA.Id, isAdmin: false);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateStoreOrderStatus_Cancel_RestocksOnlyThatStore()
    {
        var db = CreateDb();
        var (sellerA, _, _, _, soA, _, pA, pB, _) = await SeedMultiStoreOrder(db);

        // Cancel is allowed only from Pending.
        soA.Status = OrderStatus.Pending;
        await db.SaveChangesAsync();

        await NewService(db).UpdateStoreOrderStatusAsync(soA.Id,
            new UpdateOrderStatusRequest { Status = "Cancelled" }, sellerA.Id, isAdmin: false);

        (await db.Inventories.FirstAsync(i => i.ProductId == pA.Id)).Quantity.Should().Be(12); // 10 + 2 restocked
        (await db.Inventories.FirstAsync(i => i.ProductId == pB.Id)).Quantity.Should().Be(10); // store B untouched
    }

    [Fact]
    public async Task GetMyStoreOrders_ReturnsOnlyCallersStoreOrders()
    {
        var db = CreateDb();
        var (sellerA, _, _, storeA, soA, _, _, _, _) = await SeedMultiStoreOrder(db);

        var mine = await NewService(db).GetMyStoreOrdersAsync(sellerA.Id);

        mine.Should().HaveCount(1);
        mine[0].StoreOrderId.Should().Be(soA.Id);
        mine[0].StoreId.Should().Be(storeA.Id);
        mine[0].SellerNetAmount.Should().Be(20m); // seller sees their net
    }

    [Fact]
    public async Task GetMyOrders_MultiStoreOrder_SummaryIncludesPerStoreBreakdown()
    {
        var db = CreateDb();
        var (_, _, buyer, storeA, soA, soB, _, _, _) = await SeedMultiStoreOrder(db);

        var result = await NewService(db).GetMyOrdersAsync(buyer.Id, 1, 10);

        result.Items.Should().HaveCount(1);
        var summary = result.Items[0];
        summary.StoreOrders.Should().HaveCount(2);

        var breakdownA = summary.StoreOrders.Single(s => s.StoreId == storeA.Id);
        breakdownA.StoreName.Should().Be("Store A");
        breakdownA.Status.Should().Be(soA.Status.ToString());
        breakdownA.SubTotal.Should().Be(soA.SubTotal);   // 20m
        breakdownA.ItemCount.Should().Be(1);

        var breakdownB = summary.StoreOrders.Single(s => s.StoreId == soB.StoreId);
        breakdownB.StoreName.Should().Be("Store B");
        breakdownB.SubTotal.Should().Be(soB.SubTotal);   // 60m
        breakdownB.ItemCount.Should().Be(1);
    }

    [Fact]
    public async Task OrderSummaryStatus_RollsUpFromStoreOrders()
    {
        var db = CreateDb();
        var (_, _, buyer, _, soA, soB, _, _, order) = await SeedMultiStoreOrder(db);
        var service = NewService(db);

        async Task<string> Summary() => (await service.GetOrderByIdAsync(order.Id, buyer.Id, false)).Status;

        (await Summary()).Should().Be("Processing"); // both Processing

        soA.Status = OrderStatus.Shipped; await db.SaveChangesAsync();
        (await Summary()).Should().Be("Processing"); // least-advanced active

        soA.Status = OrderStatus.Delivered; soB.Status = OrderStatus.Delivered; await db.SaveChangesAsync();
        (await Summary()).Should().Be("Delivered");

        soB.Status = OrderStatus.Cancelled; await db.SaveChangesAsync(); // A Delivered, B Cancelled
        (await Summary()).Should().Be("Delivered"); // active min ignores cancelled

        soA.Status = OrderStatus.Cancelled; await db.SaveChangesAsync(); // both Cancelled
        (await Summary()).Should().Be("Cancelled");
    }
}
