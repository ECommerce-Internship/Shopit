using Shopit.Domain.Entities;
using Shopit.Domain.Enums;

namespace Shopit.Infrastructure.Data;

public static class DbInitializer
{
    public static void Seed(AppDbContext context)
    {
        if (context.Users.Any()) return; // already seeded

        var categories = new List<Category>
        {
            new() { Name = "Electronics", Slug = "electronics" },
            new() { Name = "Clothing", Slug = "clothing" },
            new() { Name = "Books", Slug = "books" }
        };

        context.Categories.AddRange(categories);
        context.SaveChanges();

        var admin = new User
        {
            FirstName = "Admin",
            LastName = "User",
            Email = "admin@shopit.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Role = UserRole.Admin
        };

        context.Users.Add(admin);
        context.SaveChanges();

        var store = new Store
        {
            Name = "Shopit Platform Store",
            Slug = "platform",
            Status = StoreStatus.Approved,
            CommissionRate = 0,
            OwnerUserId = admin.Id
        };

        context.Stores.Add(store);
        context.SaveChanges();

        var products = new List<Product>
        {
            new() { Name = "Laptop", SKU = "ELEC-001", Price = 999.99m, CategoryId = categories[0].Id, StoreId = store.Id,
                Inventory = new Inventory { Quantity = 25, LowStockThreshold = 5 } },
            new() { Name = "Phone", SKU = "ELEC-002", Price = 699.99m, CategoryId = categories[0].Id, StoreId = store.Id,
                Inventory = new Inventory { Quantity = 120, LowStockThreshold = 10 } },
            new() { Name = "T-Shirt", SKU = "CLTH-001", Price = 29.99m, CategoryId = categories[1].Id, StoreId = store.Id,
                Inventory = new Inventory { Quantity = 200, LowStockThreshold = 20 } },
            new() { Name = "Jeans", SKU = "CLTH-002", Price = 59.99m, CategoryId = categories[1].Id, StoreId = store.Id,
                Inventory = new Inventory { Quantity = 80, LowStockThreshold = 10 } },
            new() { Name = "C# Programming", SKU = "BOOK-001", Price = 39.99m, CategoryId = categories[2].Id, StoreId = store.Id,
                Inventory = new Inventory { Quantity = 50, LowStockThreshold = 5 } },
        };

        context.Products.AddRange(products);

        var coupon = new Coupon
        {
            Code = "SAVE10",
            DiscountType = CouponDiscountType.Percent,
            DiscountValue = 10,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddYears(1)
        };

        context.Coupons.Add(coupon);
        context.SaveChanges();

        // Test accounts + multi-store sample data (SCRUM-131): two sellers each owning an approved
        // store, plus a customer, so the seller/customer/admin roles all have a login and the
        // multi-store path can be exercised end-to-end.
        var seller1 = new User
        {
            FirstName = "Sarah",
            LastName = "Seller",
            Email = "seller1@shopit.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller@123"),
            Role = UserRole.Seller
        };

        var seller2 = new User
        {
            FirstName = "Sam",
            LastName = "Vendor",
            Email = "seller2@shopit.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Seller@123"),
            Role = UserRole.Seller
        };

        var customer = new User
        {
            FirstName = "Casey",
            LastName = "Customer",
            Email = "customer@shopit.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            Role = UserRole.Customer
        };

        context.Users.AddRange(seller1, seller2, customer);
        context.SaveChanges();

        var sellerStores = new List<Store>
        {
            new() { Name = "Gadget Hub", Slug = "gadget-hub", Status = StoreStatus.Approved,
                CommissionRate = 0.10m, OwnerUserId = seller1.Id },
            new() { Name = "Book Nook", Slug = "book-nook", Status = StoreStatus.Approved,
                CommissionRate = 0.15m, OwnerUserId = seller2.Id },
        };

        context.Stores.AddRange(sellerStores);
        context.SaveChanges();

        // Each seller product deliberately reuses a platform-store SKU to exercise the per-store
        // SKU uniqueness (composite (StoreId, SKU) index) — two stores may share a SKU.
        var sellerProducts = new List<Product>
        {
            new() { Name = "Wireless Earbuds", SKU = "ELEC-001", Price = 129.99m, CategoryId = categories[0].Id, StoreId = sellerStores[0].Id,
                Inventory = new Inventory { Quantity = 60, LowStockThreshold = 10 } },
            new() { Name = "Sci-Fi Novel", SKU = "BOOK-001", Price = 14.99m, CategoryId = categories[2].Id, StoreId = sellerStores[1].Id,
                Inventory = new Inventory { Quantity = 40, LowStockThreshold = 5 } },
        };

        context.Products.AddRange(sellerProducts);
        context.SaveChanges();
    }
}
