using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shopit.Domain.Entities;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.ImageSearch;

public class InMemoryImageVectorStoreTests
{
    [Fact]
    public async Task SearchAsync_RanksByCosineSimilarityDescending()
    {
        await using var db = CreateDb();
        await SeedProductWithEmbedding(db, id: 1, name: "Exact", embedding: new[] { 1f, 0f });
        await SeedProductWithEmbedding(db, id: 2, name: "Orthogonal", embedding: new[] { 0f, 1f });
        await SeedProductWithEmbedding(db, id: 3, name: "Diagonal", embedding: new[] { 0.7f, 0.7f });

        var store = new InMemoryImageVectorStore(db, NullLogger<InMemoryImageVectorStore>.Instance);

        var results = await store.SearchAsync(new[] { 1f, 0f }, topK: 10);

        results.Select(r => r.Product.Name).Should().ContainInOrder("Exact", "Diagonal", "Orthogonal");
        results[0].Score.Should().BeApproximately(1.0, 1e-6);
        results.Select(r => r.Product.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        await using var db = CreateDb();
        await SeedProductWithEmbedding(db, id: 1, name: "A", embedding: new[] { 1f, 0f });
        await SeedProductWithEmbedding(db, id: 2, name: "B", embedding: new[] { 0.9f, 0.1f });
        await SeedProductWithEmbedding(db, id: 3, name: "C", embedding: new[] { 0.8f, 0.2f });

        var store = new InMemoryImageVectorStore(db, NullLogger<InMemoryImageVectorStore>.Instance);

        var results = await store.SearchAsync(new[] { 1f, 0f }, topK: 2);

        results.Should().HaveCount(2);
        results[0].Product.Name.Should().Be("A");
    }

    [Fact]
    public async Task SearchAsync_ExcludesDeletedProducts()
    {
        await using var db = CreateDb();
        await SeedProductWithEmbedding(db, id: 1, name: "Live", embedding: new[] { 1f, 0f });
        await SeedProductWithEmbedding(db, id: 2, name: "Deleted", embedding: new[] { 1f, 0f }, isDeleted: true);

        var store = new InMemoryImageVectorStore(db, NullLogger<InMemoryImageVectorStore>.Instance);

        var results = await store.SearchAsync(new[] { 1f, 0f }, topK: 10);

        results.Should().ContainSingle().Which.Product.Name.Should().Be("Live");
    }

    [Fact]
    public async Task SearchAsync_EmptyIndex_ReturnsEmpty()
    {
        await using var db = CreateDb();
        var store = new InMemoryImageVectorStore(db, NullLogger<InMemoryImageVectorStore>.Instance);

        var results = await store.SearchAsync(new[] { 1f, 0f }, topK: 10);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_MapsProductResponseFields()
    {
        await using var db = CreateDb();
        await SeedProductWithEmbedding(db, id: 7, name: "Wireless Mouse", embedding: new[] { 1f, 0f });

        var store = new InMemoryImageVectorStore(db, NullLogger<InMemoryImageVectorStore>.Instance);

        var match = (await store.SearchAsync(new[] { 1f, 0f }, topK: 1)).Single();

        match.Product.Id.Should().Be(7);
        match.Product.Name.Should().Be("Wireless Mouse");
        match.Product.CategoryName.Should().Be("Electronics");
        match.Product.StoreName.Should().Be("Platform");
        match.Product.StockQuantity.Should().Be(5);
    }

    // ---- helpers ----

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ImageSearchTests-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task SeedProductWithEmbedding(
        AppDbContext db, int id, string name, float[] embedding, bool isDeleted = false)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == 1);
        if (category is null)
        {
            category = new Category { Id = 1, Name = "Electronics", Slug = "electronics" };
            db.Categories.Add(category);
        }

        var store = await db.Stores.FirstOrDefaultAsync(s => s.Id == 1);
        if (store is null)
        {
            store = new Store { Id = 1, Name = "Platform", Slug = "platform", OwnerUserId = 1 };
            db.Stores.Add(store);
        }

        db.Products.Add(new Product
        {
            Id = id,
            Name = name,
            Price = 10m,
            SKU = $"SKU-{id}",
            ImageUrl = $"https://example.com/{id}.jpg",
            CategoryId = 1,
            StoreId = 1,
            IsDeleted = isDeleted,
            Inventory = new Inventory { Id = id, Quantity = 5, UpdatedAt = DateTime.UtcNow }
        });

        db.ProductImageEmbeddings.Add(new ProductImageEmbedding
        {
            ProductId = id,
            Embedding = embedding,
            SourceImageUrl = $"https://example.com/{id}.jpg",
            ModelVersion = "2023-04-15",
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }
}
