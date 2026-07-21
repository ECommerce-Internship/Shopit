using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Application.DTOs.Rag;
using Shopit.Application.Rag;
using Shopit.Domain.Entities;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.Rag;

public class FeatureQaServiceTests
{
    private static readonly float[] AnyVector = { 0.1f, 0.2f, 0.3f };

    [Fact]
    public async Task AskAsync_NoChunkAboveThreshold_ReturnsFallbackWithoutCallingGemini()
    {
        var chunk = Chunk("Product Reviews", "Who can use it");
        var vectorStore = VectorStoreReturning(new ScoredChunk(chunk, 0.30));
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict); // any generation call fails the test

        var service = CreateService(vectorStore.Object, factory.Object);

        var result = await service.AskAsync("What is the capital of France?");

        result.Answered.Should().BeFalse();
        result.Sources.Should().BeEmpty();
        result.Answer.Should().NotBeNullOrWhiteSpace();
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AskAsync_EmptyCorpus_ReturnsFallback()
    {
        var vectorStore = VectorStoreReturning();
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);

        var service = CreateService(vectorStore.Object, factory.Object);

        var result = await service.AskAsync("How do reviews work?");

        result.Answered.Should().BeFalse();
        factory.Verify(f => f.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AskAsync_ChunkAboveThreshold_ReturnsGroundedAnswerWithCitations()
    {
        var chunk = Chunk("Product Reviews", "How it works", "customer",
            "docs/features/customer/product-reviews.md");
        var vectorStore = VectorStoreReturning(new ScoredChunk(chunk, 0.92));

        const string generated = "Customers who bought the product can leave a star rating and a comment.";
        var factory = FactoryReturning(GeminiEnvelope(generated));

        var service = CreateService(vectorStore.Object, factory);

        var result = await service.AskAsync("How do reviews work?");

        result.Answered.Should().BeTrue();
        result.Answer.Should().Be(generated);
        result.Sources.Should().ContainSingle();
        result.Sources[0].FeatureName.Should().Be("Product Reviews");
        result.Sources[0].SourceFile.Should().Be("docs/features/customer/product-reviews.md");
    }

    // ---- helpers ----

    private static DocumentChunk Chunk(
        string feature,
        string section,
        string audience = "customer",
        string sourceFile = "docs/features/customer/sample.md") => new()
        {
            FeatureName = feature,
            Section = section,
            Audience = audience,
            SourceFile = sourceFile,
            Content = $"{feature} > {section}\nSome body text.",
            Embedding = AnyVector,
        };

    private static Mock<IVectorStore> VectorStoreReturning(params ScoredChunk[] hits)
    {
        var store = new Mock<IVectorStore>();
        store.Setup(s => s.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);
        return store;
    }

    private static FeatureQaService CreateService(IVectorStore vectorStore, IHttpClientFactory factory)
    {
        var embedding = new Mock<IEmbeddingService>();
        embedding.Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnyVector);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "test-api-key",
                ["Gemini:Model"] = "gemini-2.5-flash",
                ["FeatureQa:TopK"] = "5",
                ["FeatureQa:RelevanceThreshold"] = "0.6",
            })
            .Build();

        return new FeatureQaService(
            embedding.Object, vectorStore, factory, config, NullLogger<FeatureQaService>.Instance);
    }

    private static IHttpClientFactory FactoryReturning(string responseJson)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            }))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        return factory.Object;
    }

    private static string GeminiEnvelope(string text)
    {
        var encoded = System.Text.Json.JsonSerializer.Serialize(text);
        return "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + encoded + "}]}}]}";
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
