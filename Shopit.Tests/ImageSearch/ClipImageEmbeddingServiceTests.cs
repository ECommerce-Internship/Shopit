using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.ImageSearch;

public class ClipImageEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedImageAsync_ValidResponse_ReturnsVectorAndModel()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK,
            """{"vector":[0.1,0.2,0.3],"model":"clip-ViT-B-32"}"""));

        var result = await service.EmbedImageAsync(ImageStream());

        result.Vector.Should().Equal(0.1f, 0.2f, 0.3f);
        result.ModelVersion.Should().Be("clip-ViT-B-32");
    }

    [Fact]
    public async Task EmbedImageAsync_EmptyImage_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, "{}"));

        var act = () => service.EmbedImageAsync(new MemoryStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*empty image*");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EmbedImageAsync_RejectedRequest_Throws(HttpStatusCode statusCode)
    {
        var service = CreateService(_ => JsonResponse(statusCode, """{"detail":"nope"}"""));

        var act = () => service.EmbedImageAsync(ImageStream());

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task EmbedImageAsync_EmptyVector_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, """{"vector":[]}"""));

        var act = () => service.EmbedImageAsync(ImageStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*empty embedding*");
    }

    [Fact]
    public async Task EmbedImageAsync_NetworkFailure_Throws()
    {
        var service = CreateService(_ => throw new HttpRequestException("boom"));

        var act = () => service.EmbedImageAsync(ImageStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*reach*");
    }

    [Fact]
    public async Task EmbedImageAsync_WarmingUpThenOk_RetriesAndSucceeds()
    {
        var calls = 0;
        var service = CreateService(_ =>
        {
            calls++;
            return calls == 1
                ? JsonResponse(HttpStatusCode.ServiceUnavailable, """{"detail":"loading"}""")
                : JsonResponse(HttpStatusCode.OK, """{"vector":[0.5,0.6],"model":"clip-ViT-B-32"}""");
        });

        var result = await service.EmbedImageAsync(ImageStream());

        calls.Should().Be(2); // retried once after the 503
        result.Vector.Should().Equal(0.5f, 0.6f);
    }

    [Fact]
    public async Task EmbedImageAsync_UnavailableBeyondRetries_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.ServiceUnavailable, """{"detail":"loading"}"""));

        var act = () => service.EmbedImageAsync(ImageStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*unavailable*");
    }

    // ---- helpers ----

    private static ClipImageEmbeddingService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("http://clip:8000/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ClipImageEmbeddingService.HttpClientName)).Returns(httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClipEmbedding:RetryBaseDelayMs"] = "1", // keep the retry tests fast
            })
            .Build();

        return new ClipImageEmbeddingService(factory.Object, config, NullLogger<ClipImageEmbeddingService>.Instance);
    }

    private static MemoryStream ImageStream() => new(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }); // JPEG magic bytes

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

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
