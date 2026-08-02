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

public class AzureImageEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedImageAsync_ValidResponse_ReturnsVectorAndModelVersion()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK,
            """{"modelVersion":"2023-04-15","vector":[0.1,0.2,0.3]}"""));

        var result = await service.EmbedImageAsync(ImageStream());

        result.Vector.Should().Equal(0.1f, 0.2f, 0.3f);
        result.ModelVersion.Should().Be("2023-04-15");
    }

    [Fact]
    public async Task EmbedImageAsync_MissingApiKey_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, "{}"), apiKey: "");

        var act = () => service.EmbedImageAsync(ImageStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*key*");
    }

    [Fact]
    public async Task EmbedImageAsync_EmptyImage_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, "{}"));

        var act = () => service.EmbedImageAsync(new MemoryStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*empty image*");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EmbedImageAsync_RejectedRequest_Throws(HttpStatusCode statusCode)
    {
        var service = CreateService(_ => JsonResponse(statusCode, """{"error":"nope"}"""));

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
    public async Task EmbedImageAsync_RateLimitedThenOk_RetriesAndSucceeds()
    {
        var calls = 0;
        var service = CreateService(_ =>
        {
            calls++;
            return calls == 1
                ? JsonResponse(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""")
                : JsonResponse(HttpStatusCode.OK, """{"vector":[0.5,0.6]}""");
        });

        var result = await service.EmbedImageAsync(ImageStream());

        calls.Should().Be(2); // retried once after the 429
        result.Vector.Should().Equal(0.5f, 0.6f);
    }

    [Fact]
    public async Task EmbedImageAsync_RateLimitedBeyondRetries_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.TooManyRequests, """{"error":"slow down"}"""));

        var act = () => service.EmbedImageAsync(ImageStream());

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*rate limit*");
    }

    // ---- helpers ----

    private static AzureImageEmbeddingService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string apiKey = "test-api-key")
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://example.cognitiveservices.azure.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(AzureImageEmbeddingService.HttpClientName)).Returns(httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureVision:ApiKey"] = apiKey,
                ["AzureVision:RetryBaseDelayMs"] = "1", // keep the retry test fast
            })
            .Build();

        return new AzureImageEmbeddingService(factory.Object, config, NullLogger<AzureImageEmbeddingService>.Instance);
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
