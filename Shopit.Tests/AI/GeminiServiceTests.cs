using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shopit.Application.AI;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Services;

namespace Shopit.Tests.AI;

public class GeminiServiceTests
{
    private const string ValidContentJson = """
        {
          "description": "A great product description that spans a couple of sentences.",
          "features": ["One", "Two", "Three", "Four", "Five"],
          "seoTitle": "Great Product - Buy Now",
          "metaDescription": "A concise meta description for the great product, well under the limit."
        }
        """;

    [Fact]
    public async Task GenerateProductContentAsync_ValidResponse_ReturnsPopulatedContent()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope(ValidContentJson)));

        var result = await service.GenerateProductContentAsync("iPhone 15 Pro Case", "Phone Accessories", "MagSafe, silicone");

        result.Should().NotBeNull();
        result.Description.Should().NotBeNullOrWhiteSpace();
        result.Features.Should().HaveCount(5);
        result.SeoTitle.Should().Be("Great Product - Buy Now");
        result.MetaDescription.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GenerateProductContentAsync_StripsMarkdownCodeFences()
    {
        var fenced = "```json\n" + ValidContentJson + "\n```";
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope(fenced)));

        var result = await service.GenerateProductContentAsync("Name", "Category", "Specs");

        result.Features.Should().HaveCount(5);
    }

    [Fact]
    public async Task GenerateProductContentAsync_MissingApiKey_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope(ValidContentJson)), apiKey: "");

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*API key*");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task GenerateProductContentAsync_RejectedRequest_Throws(HttpStatusCode statusCode)
    {
        var service = CreateService(_ => JsonResponse(statusCode, """{"error":"nope"}"""));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*API key*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_RateLimited_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.TooManyRequests, "{}"));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*rate limit*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_ServerError_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.ServiceUnavailable, "{}"));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*unavailable*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_NetworkFailure_Throws()
    {
        var service = CreateService(_ => throw new HttpRequestException("boom"));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*reach*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_EmptyCandidates_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, """{"candidates":[]}"""));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_InvalidJsonContent_Throws()
    {
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope("this is not json")));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*invalid JSON*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_WrongFeatureCount_Throws()
    {
        var fourFeatures = """
            {
              "description": "Valid description.",
              "features": ["One", "Two", "Three", "Four"],
              "seoTitle": "Title",
              "metaDescription": "Meta"
            }
            """;
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope(fourFeatures)));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*exactly 5 features*");
    }

    [Fact]
    public async Task GenerateProductContentAsync_SeoTitleTooLong_TruncatesInsteadOfThrowing()
    {
        // Gemini doesn't reliably honor the schema's maxLength constraint, so an
        // over-length title is trimmed to the limit rather than rejected outright.
        var longTitle = """
            {
              "description": "Valid description.",
              "features": ["One", "Two", "Three", "Four", "Five"],
              "seoTitle": "This SEO title is intentionally far too long to satisfy the sixty character maximum limit",
              "metaDescription": "Meta"
            }
            """;
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope(longTitle)));

        var result = await service.GenerateProductContentAsync("Name", "Category", "Specs");

        result.SeoTitle.Length.Should().BeLessThanOrEqualTo(60);
    }

    [Fact]
    public async Task GenerateProductContentAsync_EmptyDescription_Throws()
    {
        var emptyDescription = """
            {
              "description": "",
              "features": ["One", "Two", "Three", "Four", "Five"],
              "seoTitle": "Title",
              "metaDescription": "Meta"
            }
            """;
        var service = CreateService(_ => JsonResponse(HttpStatusCode.OK, GeminiEnvelope(emptyDescription)));

        var act = () => service.GenerateProductContentAsync("Name", "Category", "Specs");

        await act.Should().ThrowAsync<ExternalServiceException>()
            .WithMessage("*description*");
    }

    // ---- helpers ----

    private static GeminiService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        string apiKey = "test-api-key")
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(GeminiService.HttpClientName)).Returns(httpClient);

        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Gemini:ApiKey"]).Returns(apiKey);
        config.Setup(c => c["Gemini:Model"]).Returns("gemini-2.5-flash");

        return new GeminiService(factory.Object, config.Object, NullLogger<GeminiService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string GeminiEnvelope(string innerText)
    {
        // Embed the generated text as a JSON string literal inside the Gemini envelope.
        var encodedText = JsonSerializer.Serialize(innerText);
        return "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + encodedText + "}]}}]}";
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
