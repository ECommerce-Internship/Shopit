using Pgvector;
using Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shopit.Domain.Entities;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Shopit.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreOrder> StoreOrders => Set<StoreOrder>();
    public DbSet<StoreOrderItem> StoreOrderItems => Set<StoreOrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<UserExternalLogin> UserExternalLogins => Set<UserExternalLogin>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.Entity<Product>()
            .Property(p => p.Embedding)
            .HasColumnType("vector(3072)")
            .HasConversion(
                v => new Vector(v!),
                v => v.ToArray()
            )
        .Metadata.SetValueComparer(new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<float[]?>(
            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            v => v == null ? 0 : v.Aggregate(0, (a, b) => HashCode.Combine(a, b.GetHashCode())),
            v => v == null ? null : v.ToArray()
            ));

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

        // User -> PasswordResetTokens (one-to-many)
        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(t => t.User)
            .WithMany(u => u.PasswordResetTokens)
            .HasForeignKey(t => t.UserId);

        // User -> ExternalLogins (one-to-many)
        modelBuilder.Entity<UserExternalLogin>()
            .HasOne(el => el.User)
            .WithMany(u => u.ExternalLogins)
            .HasForeignKey(el => el.UserId);

        modelBuilder.Entity<UserExternalLogin>()
            .HasIndex(el => new { el.Provider, el.ProviderUserId }).IsUnique();

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

        modelBuilder.Entity<Inventory>()
        .Property(i => i.Version)
        .IsConcurrencyToken();

        // Product <-> Inventory (one-to-one)
        modelBuilder.Entity<Inventory>()
            .HasOne(i => i.Product)
            .WithOne(p => p.Inventory)
            .HasForeignKey<Inventory>(i => i.ProductId);

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

        // User -> Stores (one-to-many, owner)
        modelBuilder.Entity<Store>()
            .HasOne(s => s.Owner)
            .WithMany(u => u.Stores)
            .HasForeignKey(s => s.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Store -> Products (one-to-many)
        modelBuilder.Entity<Product>()
            .HasOne(p => p.Store)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        // Store -> Coupons (one-to-many, nullable: null = platform-wide)
        modelBuilder.Entity<Coupon>()
            .HasOne(c => c.Store)
            .WithMany(s => s.Coupons)
            .HasForeignKey(c => c.StoreId)
            .OnDelete(DeleteBehavior.SetNull);

        // Order -> StoreOrders (one-to-many)
        modelBuilder.Entity<StoreOrder>()
            .HasOne(so => so.Order)
            .WithMany(o => o.StoreOrders)
            .HasForeignKey(so => so.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Store -> StoreOrders (one-to-many)
        modelBuilder.Entity<StoreOrder>()
            .HasOne(so => so.Store)
            .WithMany(s => s.StoreOrders)
            .HasForeignKey(so => so.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        // StoreOrder -> StoreOrderItems (one-to-many)
        modelBuilder.Entity<StoreOrderItem>()
            .HasOne(soi => soi.StoreOrder)
            .WithMany(so => so.StoreOrderItems)
            .HasForeignKey(soi => soi.StoreOrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Product -> StoreOrderItems (one-to-many)
        modelBuilder.Entity<StoreOrderItem>()
            .HasOne(soi => soi.Product)
            .WithMany(p => p.StoreOrderItems)
            .HasForeignKey(soi => soi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order <-> Payment (one-to-one)
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Order)
            .WithOne(o => o.Payment)
            .HasForeignKey<Payment>(p => p.OrderId);

        // Unique constraints
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.StoreId, p.SKU }).IsUnique();

        modelBuilder.Entity<Coupon>()
            .HasIndex(c => c.Code).IsUnique();

        modelBuilder.Entity<Category>()
            .HasIndex(c => c.Slug).IsUnique();

        modelBuilder.Entity<Store>()
            .HasIndex(s => s.Slug).IsUnique();

        modelBuilder.Entity<Review>()
            .HasIndex(r => new { r.UserId, r.ProductId }).IsUnique();

        // Decimal precision
        modelBuilder.Entity<Product>()
            .Property(p => p.Price).HasPrecision(10, 2);

        modelBuilder.Entity<Coupon>()
            .Property(c => c.DiscountValue).HasPrecision(10, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.TotalAmount).HasPrecision(10, 2);

        modelBuilder.Entity<StoreOrderItem>()
            .Property(soi => soi.UnitPrice).HasPrecision(10, 2);

        modelBuilder.Entity<StoreOrderItem>()
            .Property(soi => soi.Subtotal).HasPrecision(10, 2);

        modelBuilder.Entity<StoreOrder>()
            .Property(so => so.SubTotal).HasPrecision(10, 2);

        modelBuilder.Entity<StoreOrder>()
            .Property(so => so.CommissionAmount).HasPrecision(10, 2);

        modelBuilder.Entity<StoreOrder>()
            .Property(so => so.SellerNetAmount).HasPrecision(10, 2);

        modelBuilder.Entity<Store>()
            .Property(s => s.CommissionRate).HasPrecision(5, 4);

        modelBuilder.Entity<Payment>()
            .Property(p => p.Amount).HasPrecision(10, 2);

        modelBuilder.Entity<Coupon>()
            .Property(c => c.MinimumOrderAmount).HasPrecision(10, 2);

        modelBuilder.Entity<Order>()
            .Property(o => o.DiscountAmount).HasPrecision(10, 2);
    }
}