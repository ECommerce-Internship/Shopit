using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shopit.Domain.Entities;

namespace Shopit.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User -> Orders (one-to-many)
        modelBuilder.Entity<Order>()
            .HasOne(o => o.User)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId);

        // User -> Carts (one-to-many)
        modelBuilder.Entity<Cart>()
            .HasOne(c => c.User)
            .WithMany(u => u.Carts)
            .HasForeignKey(c => c.UserId);

        // User -> Reviews (one-to-many)
        modelBuilder.Entity<Review>()
            .HasOne(r => r.User)
            .WithMany(u => u.Reviews)
            .HasForeignKey(r => r.UserId);

        // User -> RefreshTokens (one-to-many)
        modelBuilder.Entity<RefreshToken>()
            .HasOne(rt => rt.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId);

        // Category self-referencing (parent -> subcategories)
        modelBuilder.Entity<Category>()
            .HasOne(c => c.ParentCategory)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Category -> Products (one-to-many)
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId);

        // Product <-> Inventory (one-to-one)
        modelBuilder.Entity<Inventory>()
            .HasOne(i => i.Product)
            .WithOne(p => p.Inventory)
            .HasForeignKey<Inventory>(i => i.ProductId);

        modelBuilder.Entity<Inventory>()
            .Property(i => i.RowVersion)
            .IsRowVersion();

        // Cart -> CartItems (one-to-many)
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Cart)
            .WithMany(c => c.CartItems)
            .HasForeignKey(ci => ci.CartId);

        // Product -> CartItems (one-to-many)
        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Product)
            .WithMany(p => p.CartItems)
            .HasForeignKey(ci => ci.ProductId);

        // Order -> OrderItems (one-to-many)
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(oi => oi.OrderId);

        // Product -> OrderItems (one-to-many)
        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(oi => oi.ProductId);

        // Order <-> Payment (one-to-one)
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId);

        // Unique constraints
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<Product>()
            .HasIndex(p => p.SKU).IsUnique();

        modelBuilder.Entity<Coupon>()
            .HasIndex(c => c.Code).IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Slug).IsUnique();

        modelBuilder.Entity<Review>()
            .HasIndex(r => new { r.UserId, r.ProductId }).IsUnique();

        // SEO fields
        modelBuilder.Entity<Product>()
            .Property(p => p.SeoTitle).HasMaxLength(60);

        modelBuilder.Entity<Product>()
            .Property(p => p.MetaDescription).HasMaxLength(155);

        modelBuilder.Entity<Product>()
            .Property(p => p.Features)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null));

        // Decimal precision
        modelBuilder.Entity<Product>()
            .Property(p => p.Price).HasPrecision(10, 2);

        modelBuilder.Entity<Coupon>()
            .Property(c => c.DiscountValue).HasPrecision(10, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalAmount).HasPrecision(10, 2);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.UnitPrice).HasPrecision(10, 2);

        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.Subtotal).HasPrecision(10, 2);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Amount).HasPrecision(10, 2);

        modelBuilder.Entity<Coupon>()
            .Property(c => c.MinimumOrderAmount).HasPrecision(10, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.DiscountAmount).HasPrecision(10, 2);
    }
}