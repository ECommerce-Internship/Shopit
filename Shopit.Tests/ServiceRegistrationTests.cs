using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Shopit.Tests;

public class ServiceRegistrationTests
{
    [Fact]
    public void Should_Not_Register_Any_ShopitService_More_Than_Once()
    {
        IServiceCollection? capturedServices = null;

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    capturedServices = services;
                });
            });

        // Accessing Services forces the host (and Program.cs's builder.Build()) to run,
        // which triggers the ConfigureServices callback above.
        using var scope = factory.Services.CreateScope();

        capturedServices.Should().NotBeNull();

        var duplicates = capturedServices!
            .Where(d => d.ServiceType.Namespace != null && d.ServiceType.Namespace.StartsWith("Shopit"))
            .GroupBy(d => d.ServiceType)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.FullName} ({g.Count()} registrations)")
            .ToList();

        duplicates.Should().BeEmpty(
            "each Shopit service interface should be registered exactly once in Program.cs, but found duplicates: {0}",
            string.Join(", ", duplicates));
    }
}