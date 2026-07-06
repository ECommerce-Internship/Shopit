using Shopit.Application.Interfaces;
using Serilog;

namespace Shopit.Infrastructure.Services;

public class EmailService : IEmailService
{
    public Task SendOrderConfirmationAsync(int orderId, string email)
    {
        Log.Information("Order confirmation email sent to {Email} for Order {OrderId}", email, orderId);
        return Task.CompletedTask;
    }

    public Task SendLowStockAlertAsync(string adminEmail, string productName, int currentQty, int threshold)
    {
        Log.Warning("Low stock alert email sent to {Email} for {ProductName}", adminEmail, productName);
        return Task.CompletedTask;
    }
}