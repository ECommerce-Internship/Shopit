using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Application.DTOs.ImageSearch;
using Shopit.Application.Interfaces;
using Shopit.Application.Products.DTOs;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.ImageSearch;

public class ProductImageSearchServiceTests
{
    [Fact]
    public async Task SearchByImageAsync_NullImage_ThrowsValidation()
    {
        var service = CreateService(out _, out _);

        var act = () => service.SearchByImageAsync(null!, topK: 10);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*required*");
    }

    [Fact]
    public async Task SearchByImageAsync_EmptyImage_ThrowsValidation()
    {
        var service = CreateService(out _, out _);

        var act = () => service.SearchByImageAsync(FakeImage(Array.Empty<byte>()), topK: 10);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*required*");
    }

    [Fact]
    public async Task SearchByImageAsync_NonImageContentType_ThrowsValidation()
    {
        var service = CreateService(out _, out _);

        var act = () => service.SearchByImageAsync(FakeImage(new byte[] { 1, 2, 3 }, contentType: "application/pdf"), topK: 10);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*must be an image*");
    }

    [Fact]
    public async Task SearchByImageAsync_OversizedImage_ThrowsValidation()
    {
        var service = CreateService(out _, out _);

        var act = () => service.SearchByImageAsync(FakeImage(new byte[] { 1 }, length: 21L * 1024 * 1024), topK: 10);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("*20MB*");
    }

    [Fact]
    public async Task SearchByImageAsync_ValidImage_EmbedsAndReturnsMatches()
    {
        var service = CreateService(out var embedding, out var store);

        embedding.Setup(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageEmbeddingResult(new[] { 1f, 0f }, "2023-04-15"));

        var expected = new ImageSearchMatchResponse(new ProductResponse { Id = 42, Name = "Match" }, 0.99);
        store.Setup(s => s.SearchAsync(It.IsAny<float[]>(), 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { expected });

        var result = await service.SearchByImageAsync(FakeImage(new byte[] { 1, 2, 3 }), topK: 10);

        result.Matches.Should().ContainSingle().Which.Product.Id.Should().Be(42);
        embedding.Verify(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchByImageAsync_ClampsTopKToMax()
    {
        var service = CreateService(out var embedding, out var store);
        embedding.Setup(e => e.EmbedImageAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageEmbeddingResult(new[] { 1f }, "v"));
        store.Setup(s => s.SearchAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ImageSearchMatchResponse>());

        await service.SearchByImageAsync(FakeImage(new byte[] { 1 }), topK: 9999);

        // 50 is the service's max; an absurd request is clamped, not passed through.
        store.Verify(s => s.SearchAsync(It.IsAny<float[]>(), 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- helpers ----

    private static ProductImageSearchService CreateService(
        out Mock<IImageEmbeddingService> embedding,
        out Mock<IImageVectorStore> store)
    {
        embedding = new Mock<IImageEmbeddingService>();
        store = new Mock<IImageVectorStore>();
        return new ProductImageSearchService(
            embedding.Object, store.Object, NullLogger<ProductImageSearchService>.Instance);
    }

    private static IFormFile FakeImage(byte[] content, string contentType = "image/jpeg", long? length = null)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, length ?? content.Length, "image", "photo.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
