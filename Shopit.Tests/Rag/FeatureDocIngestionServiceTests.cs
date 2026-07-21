using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Application.Rag;
using Shopit.Infrastructure.Data;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.Rag;

public class FeatureDocIngestionServiceTests : IDisposable
{
    private readonly string _docsRoot;

    private const string Sample =
        "# Product Reviews\n\n" +
        "## What it does\nCustomers rate products.\n\n" +
        "## Who can use it\nVerified buyers only.\n";

    public FeatureDocIngestionServiceTests()
    {
        _docsRoot = Path.Combine(Path.GetTempPath(), "shopit-rag-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(_docsRoot, "customer"));
        File.WriteAllText(Path.Combine(_docsRoot, "customer", "product-reviews.md"), Sample);
    }

    [Fact]
    public async Task ReindexAsync_FirstRun_EmbedsEveryChunk()
    {
        await using var db = CreateDb();
        var embedding = CountingEmbedding();
        var service = CreateService(db, embedding.Object);

        var result = await service.ReindexAsync();

        result.Files.Should().Be(1);
        result.Chunks.Should().Be(2);
        result.Embedded.Should().Be(2);
        result.Skipped.Should().Be(0);
        (await db.DocumentChunks.CountAsync()).Should().Be(2);
        embedding.Verify(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ReindexAsync_SecondRunUnchanged_SkipsReembedding()
    {
        await using var db = CreateDb();
        var embedding = CountingEmbedding();
        var service = CreateService(db, embedding.Object);

        await service.ReindexAsync();
        embedding.Invocations.Clear();

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(0);
        result.Skipped.Should().Be(2);
        embedding.Verify(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReindexAsync_ContentEdited_ReembedsOnlyChangedChunk()
    {
        await using var db = CreateDb();
        var embedding = CountingEmbedding();
        var service = CreateService(db, embedding.Object);

        await service.ReindexAsync();
        embedding.Invocations.Clear();

        // Edit one section; the other is untouched.
        File.WriteAllText(
            Path.Combine(_docsRoot, "customer", "product-reviews.md"),
            Sample.Replace("Verified buyers only.", "Any signed-in customer who purchased the item."));

        var result = await service.ReindexAsync();

        result.Embedded.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task ReindexAsync_MissingDocsDirectory_ReturnsEmptyResult()
    {
        await using var db = CreateDb();
        var embedding = CountingEmbedding();
        var service = CreateService(db, embedding.Object, docsPath: Path.Combine(_docsRoot, "does-not-exist"));

        var result = await service.ReindexAsync();

        result.Should().Be(new Application.DTOs.Rag.IngestionResult(0, 0, 0, 0));
    }

    // ---- helpers ----

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<IEmbeddingService> CountingEmbedding()
    {
        var embedding = new Mock<IEmbeddingService>();
        embedding.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 0.1f, 0.2f, 0.3f });
        return embedding;
    }

    private FeatureDocIngestionService CreateService(
        AppDbContext db,
        IEmbeddingService embedding,
        string? docsPath = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FeatureQa:DocsPath"] = docsPath ?? _docsRoot,
            })
            .Build();

        return new FeatureDocIngestionService(
            db, embedding, new MarkdownFeatureChunker(), config,
            NullLogger<FeatureDocIngestionService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_docsRoot))
            Directory.Delete(_docsRoot, recursive: true);
    }
}
