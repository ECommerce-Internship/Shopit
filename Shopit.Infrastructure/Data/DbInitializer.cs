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

        var products = new List<Product>
        {
            new() { Name = "Laptop", SKU = "ELEC-001", Price = 999.99m, CategoryId = categories[0].Id,
                Inventory = new Inventory { Quantity = 25, LowStockThreshold = 5 } },
            new() { Name = "Phone", SKU = "ELEC-002", Price = 699.99m, CategoryId = categories[0].Id,
                Inventory = new Inventory { Quantity = 120, LowStockThreshold = 10 } },
            new() { Name = "T-Shirt", SKU = "CLTH-001", Price = 29.99m, CategoryId = categories[1].Id,
                Inventory = new Inventory { Quantity = 200, LowStockThreshold = 20 } },
            new() { Name = "Jeans", SKU = "CLTH-002", Price = 59.99m, CategoryId = categories[1].Id,
                Inventory = new Inventory { Quantity = 80, LowStockThreshold = 10 } },
            new() { Name = "C# Programming", SKU = "BOOK-001", Price = 39.99m, CategoryId = categories[2].Id,
                Inventory = new Inventory { Quantity = 50, LowStockThreshold = 5 } },
        };

        context.Products.AddRange(products);

        var admin = new User
        {
            FirstName = "Admin",
            LastName = "User",
            Email = "admin@shopit.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Role = UserRole.Admin
        };

        context.Users.Add(admin);

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
    }
}