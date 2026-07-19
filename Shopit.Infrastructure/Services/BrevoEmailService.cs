using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

/// <summary>
/// Sends transactional email through the Brevo (formerly Sendinblue) HTTP API
/// (POST https://api.brevo.com/v3/smtp/email, authenticated with an "api-key" header).
/// Failures are logged, never thrown, so a mail outage cannot break the calling flow
/// (order placement, password reset, etc.) — matching the previous email behaviour.
/// </summary>
public class BrevoEmailService : IEmailService
{
    private const string SendEndpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BrevoEmailService> _logger;
    private readonly string _apiKey;
    private readonly string _fromEmail;
    private readonly string _fromName;

    public BrevoEmailService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BrevoEmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _apiKey = configuration["Brevo:ApiKey"] ?? string.Empty;
        _fromEmail = configuration["Brevo:FromEmail"] ?? string.Empty;
        _fromName = string.IsNullOrWhiteSpace(configuration["Brevo:FromName"])
            ? "Shopit"
            : configuration["Brevo:FromName"]!;
    }

    public Task SendOrderConfirmationAsync(int orderId, string email)
    {
        var htmlContent = $"""
            <h2>Order Confirmation #{orderId}</h2>
            <p>Thank you for your order! Your order #{orderId} has been confirmed.</p>
            """;
        return SendAsync(email, $"Order Confirmation #{orderId}",
            htmlContent, $"Thank you for your order #{orderId}.");
    }

    public Task SendLowStockAlertAsync(string adminEmail, string productName, int currentQty, int threshold)
    {
        var htmlContent = $"""
            <h2>Low Stock Alert</h2>
            <p>The following product is running low on stock:</p>
            <table border='1' cellpadding='8' cellspacing='0'>
                <tr><th>Product</th><th>Current Stock</th><th>Threshold</th></tr>
                <tr>
                    <td>{productName}</td>
                    <td style='color:red;'><strong>{currentQty}</strong></td>
                    <td>{threshold}</td>
                </tr>
            </table>
            <p>Please restock as soon as possible.</p>
            """;
        return SendAsync(adminEmail, $"Low Stock Alert: {productName}",
            htmlContent, $"Low stock alert: {productName} has {currentQty} units remaining (threshold: {threshold}).");
    }

    public Task SendPasswordResetCodeAsync(string email, string code)
    {
        var htmlContent = $"""
            <h2>Reset your password</h2>
            <p>Use the following code to reset your password. It expires in 15 minutes.</p>
            <p style='font-size:24px;font-weight:bold;letter-spacing:4px;'>{code}</p>
            <p>If you didn't request a password reset, you can safely ignore this email.</p>
            """;
        return SendAsync(email, "Your password reset code",
            htmlContent, $"Your password reset code is {code}. It expires in 15 minutes.");
    }

    private async Task SendAsync(string toEmail, string subject, string htmlContent, string textContent)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Brevo API key is not configured; skipping email '{Subject}' to {Email}.",
                subject, toEmail);
            return;
        }

        var payload = new BrevoEmailRequest
        {
            Sender = new BrevoContact { Name = _fromName, Email = _fromEmail },
            To = new[] { new BrevoContact { Email = toEmail } },
            Subject = subject,
            HtmlContent = htmlContent,
            TextContent = textContent
        };

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("api-key", _apiKey);
        request.Headers.Add("accept", "application/json");

        try
        {
            using var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Brevo returned {Status} sending '{Subject}' to {Email}: {Body}",
                    response.StatusCode, subject, toEmail, body);
                return;
            }

            _logger.LogInformation("Email '{Subject}' sent to {Email} via Brevo - Status {Status}",
                subject, toEmail, response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Brevo while sending '{Subject}' to {Email}.", subject, toEmail);
        }
    }

    private sealed class BrevoEmailRequest
    {
        [JsonPropertyName("sender")] public BrevoContact Sender { get; set; } = default!;
        [JsonPropertyName("to")] public BrevoContact[] To { get; set; } = default!;
        [JsonPropertyName("subject")] public string Subject { get; set; } = default!;
        [JsonPropertyName("htmlContent")] public string HtmlContent { get; set; } = default!;
        [JsonPropertyName("textContent")] public string TextContent { get; set; } = default!;
    }

    private sealed class BrevoContact
    {
        [JsonPropertyName("name")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; set; }

        [JsonPropertyName("email")] public string Email { get; set; } = default!;
    }
}
