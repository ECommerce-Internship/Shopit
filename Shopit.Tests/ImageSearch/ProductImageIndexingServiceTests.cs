using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.ImageSearch;

public class ProductImageIndexingServiceTests
{
    [Fact]
    public async Task ReindexAsync_FirstRun_EmbedsEveryProductWithImage()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");
        await SeedProduct(db, 2, imageUrl: "https://blob/2.jpg");
        await SeedProduct(db, 3, imageUrl: null); // no image → not indexed

        var embedding = OkEmbedding();
        var service = CreateService(db, embedding.Object, DownloadsOk());

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(2);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        (await db.ProductImageEmbeddings.CountAsync()).Should().Be(2);
        embedding.Verify(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReindexAsync_SecondRunUnchanged_SkipsReembedding()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");

        var embedding = OkEmbedding();
        var service = CreateService(db, embedding.Object, DownloadsOk());

        await service.ReindexAsync();
        embedding.Invocations.Clear();

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(0);
        result.Skipped.Should().Be(1);
        embedding.Verify(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReindexAsync_ImageUrlChanged_ReembedsThatProduct()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");

        var embedding = OkEmbedding();
        var service = CreateService(db, embedding.Object, DownloadsOk());
        await service.ReindexAsync();

        var product = await db.Products.SingleAsync(p => p.Id == 1);
        product.ImageUrl = "https://blob/1-v2.jpg";
        await db.SaveChangesAsync();

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(1);
        result.Skipped.Should().Be(0);
        (await db.ProductImageEmbeddings.SingleAsync()).SourceImageUrl.Should().Be("https://blob/1-v2.jpg");
    }

    [Fact]
    public async Task ReindexAsync_ForceWithUnchangedUrl_ReembedsEveryProduct()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");
        await SeedProduct(db, 2, imageUrl: "https://blob/2.jpg");

        var embedding = OkEmbedding();
        var service = CreateService(db, embedding.Object, DownloadsOk());

        // First run indexes both; a plain second run would skip both (unchanged URLs).
        await service.ReindexAsync();
        embedding.Invocations.Clear();

        // force=true must re-embed every product despite the URLs being unchanged —
        // the path used after switching the embedding provider (CLIP → Jina).
        var result = await service.ReindexAsync(force: true);

        result.Embedded.Should().Be(2);
        result.Skipped.Should().Be(0);
        embedding.Verify(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReindexAsync_DownloadFails_CountsAsFailedAndContinues()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");
        await SeedProduct(db, 2, imageUrl: "https://blob/2.jpg");

        var embedding = OkEmbedding();
        // Fail the download for product 1's URL, succeed for the rest.
        var handler = new StubHttpMessageHandler(req =>
            req.RequestUri!.AbsoluteUri.Contains("/1.jpg")
                ? throw new HttpRequestException("404")
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });

        var service = CreateService(db, embedding.Object, handler);

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(1);
        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task ReindexAsync_AzureFails_CountsAsFailed()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");

        var embedding = new Mock<IImageEmbeddingService>();
        embedding.Setup(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("azure down"));

        var service = CreateService(db, embedding.Object, DownloadsOk());

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(0);
        result.Failed.Should().Be(1);
        (await db.ProductImageEmbeddings.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ReindexAsync_ProductImageRemoved_RemovesStaleEmbedding()
    {
        await using var db = CreateDb();
        await SeedProduct(db, 1, imageUrl: "https://blob/1.jpg");

        var embedding = OkEmbedding();
        var service = CreateService(db, embedding.Object, DownloadsOk());
        await service.ReindexAsync();

        var product = await db.Products.SingleAsync(p => p.Id == 1);
        product.ImageUrl = null;
        await db.SaveChangesAsync();

        var result = await service.ReindexAsync();

        result.Removed.Should().Be(1);
        (await db.ProductImageEmbeddings.CountAsync()).Should().Be(0);
    }

    // ---- helpers ----

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ImageIndexTests-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<IImageEmbeddingService> OkEmbedding()
    {
        var embedding = new Mock<IImageEmbeddingService>();
        embedding.Setup(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageEmbeddingResult(new[] { 0.1f, 0.2f }, "2023-04-15"));
        return embedding;
    }

    private static StubHttpMessageHandler DownloadsOk() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });

    private static ProductImageIndexingService CreateService(
        AppDbContext db, IImageEmbeddingService embedding, HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureVision:IndexDelayMs"] = "0", // no throttle wait in tests
            })
            .Build();

        return new ProductImageIndexingService(
            db, embedding, factory.Object, config, NullLogger<ProductImageIndexingService>.Instance);
    }

    private static async Task SeedProduct(AppDbContext db, int id, string? imageUrl)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == 1))
            db.Categories.Add(new Category { Id = 1, Name = "Electronics", Slug = "electronics" });
        if (!await db.Stores.AnyAsync(s => s.Id == 1))
            db.Stores.Add(new Store { Id = 1, Name = "Platform", Slug = "platform", OwnerUserId = 1 });

        db.Products.Add(new Product
        {
            Id = id,
            Name = $"Product {id}",
            Price = 10m,
            SKU = $"SKU-{id}",
            ImageUrl = imageUrl,
            CategoryId = 1,
            StoreId = 1
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
