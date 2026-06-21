using Microsoft.Extensions.Configuration;
using Serilog;
using SendGrid;
using SendGrid.Helpers.Mail;
using Shopit.Application.Interfaces;
using Shopit.Application.Models;

namespace Shopit.Infrastructure.Services;

public class SendGridEmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public SendGridEmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

  public async Task SendOrderConfirmationAsync(int orderId, string email)
{
    var apiKey = _configuration["SendGrid:ApiKey"];
    var fromEmail = _configuration["SendGrid:FromEmail"]!;
    var fromName = _configuration["SendGrid:FromName"]!;

    var client = new SendGridClient(apiKey);

    var htmlContent = $"""
        <h2>Order Confirmation #{orderId}</h2>
        <p>Thank you for your order! Your order #{orderId} has been confirmed.</p>
        """;

    var msg = MailHelper.CreateSingleEmail(
        new EmailAddress(fromEmail, fromName),
        new EmailAddress(email),
        $"Order Confirmation #{orderId}",
        $"Thank you for your order #{orderId}.",
        htmlContent);

    var response = await client.SendEmailAsync(msg);
    Log.Information("Order confirmation email sent to {Email} for Order {OrderId} - Status {Status}",
        email, orderId, response.StatusCode);
}

    public async Task SendLowStockAlertAsync(string adminEmail, string productName, int currentQty, int threshold)
    {
        var apiKey = _configuration["SendGrid:ApiKey"];
        Console.WriteLine($"=== API Key first 10 chars: {apiKey?.Substring(0, Math.Min(10, apiKey?.Length ?? 0))} ===");
        var fromEmail = _configuration["SendGrid:FromEmail"]!;
        var fromName = _configuration["SendGrid:FromName"]!;

        var client = new SendGridClient(apiKey);

        var htmlContent = $"""
            <h2>⚠️ Low Stock Alert</h2>
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

        var msg = MailHelper.CreateSingleEmail(
            new EmailAddress(fromEmail, fromName),
            new EmailAddress(adminEmail),
            $"Low Stock Alert: {productName}",
            $"Low stock alert: {productName} has {currentQty} units remaining (threshold: {threshold})",
            htmlContent);

        var response = await client.SendEmailAsync(msg);
        Console.WriteLine($"=== SendGrid response: {response.StatusCode} ===");
        var responseBody = await response.Body.ReadAsStringAsync();
        Console.WriteLine($"=== SendGrid body: {responseBody} ===");
        Console.WriteLine($"=== Low stock alert sent to {adminEmail} for {productName} - Status {response.StatusCode} ===");
            
    }
}