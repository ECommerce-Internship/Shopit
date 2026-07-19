using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

public class EmailServiceStub : IEmailService
{
    public Task SendOrderConfirmationAsync(int orderId, string email)
    {
        return Task.CompletedTask;
    }

    public Task SendLowStockAlertAsync(string adminEmail, string productName, int currentQty, int threshold)
    {
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(string email, string code)
    {
        return Task.CompletedTask;
    }
}