using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

public class EmailServiceStub : IEmailService
{
    public Task SendOrderConfirmationAsync(int orderId, string email)
    {
        // TODO: implement real email sending
        return Task.CompletedTask;
    }
}