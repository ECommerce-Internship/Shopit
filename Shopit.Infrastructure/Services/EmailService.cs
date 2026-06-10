using Serilog;
using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

public class EmailService : IEmailService
{
    public Task SendOrderConfirmationAsync(int orderId, string userEmail)
    {
        Log.Information(
            "EMAIL SENT: Order confirmation for Order {OrderId} sent to {Email}",
            orderId,
            userEmail);
        return Task.CompletedTask;
    }
}