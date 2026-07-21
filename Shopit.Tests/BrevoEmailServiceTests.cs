using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Shopit.Infrastructure.Services;
using Xunit;

namespace Shopit.Tests;

public class BrevoEmailServiceTests
{
    // Captures the outgoing request so assertions can inspect endpoint, headers and body.
    private static (BrevoEmailService service, Mock<HttpMessageHandler> handler) CreateService(
        Dictionary<string, string?> config, HttpStatusCode responseStatus = HttpStatusCode.Created)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(responseStatus) { Content = new StringContent("{}") });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var service = new BrevoEmailService(factory.Object, configuration, NullLogger<BrevoEmailService>.Instance);
        return (service, handler);
    }

    private static Dictionary<string, string?> ConfiguredKey() => new()
    {
        { "Brevo:ApiKey", "test-key" },
        { "Brevo:FromEmail", "noreply@shopit.com" },
        { "Brevo:FromName", "Shopit" }
    };

    [Fact]
    public async Task SendPasswordResetCodeAsync_PostsToBrevoWithKeyAndCode()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var (service, handler) = CreateService(ConfiguredKey());
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage req, CancellationToken _) =>
            {
                captured = req;
                body = await req.Content!.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") };
            });

        await service.SendPasswordResetCodeAsync("user@test.com", "123456");

        captured.Should().NotBeNull();
        captured!.Method.Should().Be(HttpMethod.Post);
        captured.RequestUri!.ToString().Should().Be("https://api.brevo.com/v3/smtp/email");
        captured.Headers.GetValues("api-key").Should().ContainSingle().Which.Should().Be("test-key");
        body.Should().Contain("user@test.com").And.Contain("123456").And.Contain("noreply@shopit.com");
    }

    [Fact]
    public async Task SendAsync_NoApiKey_SkipsHttpCallAndDoesNotThrow()
    {
        var (service, handler) = CreateService(new Dictionary<string, string?> { { "Brevo:FromEmail", "noreply@shopit.com" } });

        var act = async () => await service.SendPasswordResetCodeAsync("user@test.com", "123456");

        await act.Should().NotThrowAsync();
        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_BrevoReturnsError_DoesNotThrow()
    {
        var (service, _) = CreateService(ConfiguredKey(), HttpStatusCode.Unauthorized);

        var act = async () => await service.SendPasswordResetCodeAsync("user@test.com", "123456");

        // Mail failures are logged, never thrown, so the calling flow is unaffected.
        await act.Should().NotThrowAsync();
    }
}
