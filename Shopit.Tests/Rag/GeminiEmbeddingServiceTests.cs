using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests.Rag;

public class GeminiEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_ValidResponse_ReturnsVector()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK,
            """{"embedding":{"values":[0.1,0.2,0.3]}}"""));

        var result = await service.EmbedAsync("how do reviews work?");

        result.Should().Equal(0.1f, 0.2f, 0.3f);
    }

    [Fact]
    public async Task EmbedAsync_MissingApiKey_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, "{}"), apiKey: "");

        var act = () => service.EmbedAsync("text");

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*API key*");
    }

    [Fact]
    public async Task EmbedAsync_EmptyText_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, "{}"));

        var act = () => service.EmbedAsync("   ");

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*empty*");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task EmbedAsync_RejectedRequest_Throws(HttpStatusCode statusCode)
    {
        var service = CreateService(_ => JsonResponse(statusCode, """{"error":"nope"}"""));

        var act = () => service.EmbedAsync("text");

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    [Fact]
    public async Task EmbedAsync_EmptyValues_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, """{"embedding":{"values":[]}}"""));

        var act = () => service.EmbedAsync("text");

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*empty embedding*");
    }

    [Fact]
    public async Task EmbedAsync_NetworkFailure_Throws()
    {
        var service = CreateService(_ => throw new HttpRequestException("boom"));

        var act = () => service.EmbedAsync("text");

        await act.Should().ThrowAsync<ExternalServiceException>().WithMessage("*reach*");
    }

    // ---- helpers ----

    private static GeminiEmbeddingService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string apiKey = "test-api-key")
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(GeminiEmbeddingService.HttpClientName)).Returns(httpClient);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Gemini:ApiKey"]).Returns(apiKey);
        config.Setup(c => c["Gemini:EmbeddingModel"]).Returns("text-embedding-004");

        return new GeminiEmbeddingService(factory.Object, config.Object, NullLogger<GeminiEmbeddingService>.Instance);
    }

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
