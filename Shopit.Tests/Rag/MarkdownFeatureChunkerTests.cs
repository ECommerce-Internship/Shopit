using FluentAssertions;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.Rag;

public class MarkdownFeatureChunkerTests
{
    private readonly MarkdownFeatureChunker _chunker = new();

    private const string Sample =
        "# Managing Products\n" +
        "\n" +
        "## What it does\n" +
        "Sellers create, edit, and remove products.\n" +
        "\n" +
        "## Endpoints\n" +
        "| Method | Route |\n" +
        "|---|---|\n" +
        "| POST | /api/v1/products |\n";

    [Fact]
    public void Chunk_SplitsBySection()
    {
        var chunks = _chunker.Chunk(Sample);

        chunks.Should().HaveCount(2);
        chunks.Select(c => c.Section).Should().ContainInOrder("What it does", "Endpoints");
    }

    [Fact]
    public void Chunk_CapturesFeatureNameFromH1()
    {
        var chunks = _chunker.Chunk(Sample);

        chunks.Should().OnlyContain(c => c.FeatureName == "Managing Products");
    }

    [Fact]
    public void Chunk_PrependsBreadcrumbToContent()
    {
        var chunks = _chunker.Chunk(Sample);

        var whatItDoes = chunks.Single(c => c.Section == "What it does");
        whatItDoes.Content.Should().StartWith("Managing Products > What it does\n");
        whatItDoes.Content.Should().Contain("Sellers create, edit, and remove products.");
    }

    [Fact]
    public void Chunk_KeepsMarkdownTableIntact()
    {
        var chunks = _chunker.Chunk(Sample);

        var endpoints = chunks.Single(c => c.Section == "Endpoints");
        endpoints.Content.Should().Contain("| Method | Route |");
        endpoints.Content.Should().Contain("| POST | /api/v1/products |");
    }

    [Fact]
    public void Chunk_HashIsStableForSameContent()
    {
        var first = _chunker.Chunk(Sample);
        var second = _chunker.Chunk(Sample);

        first.Select(c => c.ContentHash).Should().Equal(second.Select(c => c.ContentHash));
    }

    [Fact]
    public void Chunk_HashDiffersWhenContentChanges()
    {
        var original = _chunker.Chunk(Sample).First().ContentHash;
        var edited = _chunker.Chunk(Sample.Replace("remove products", "archive products")).First().ContentHash;

        edited.Should().NotBe(original);
    }

    [Fact]
    public void Chunk_SkipsEmptySections()
    {
        var chunks = _chunker.Chunk("# Title\n\n## Empty\n\n## Filled\nBody here.\n");

        chunks.Should().ContainSingle();
        chunks.Single().Section.Should().Be("Filled");
    }
}
