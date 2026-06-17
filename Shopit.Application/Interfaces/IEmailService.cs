namespace Shopit.Application.Interfaces;

public interface IEmailService
{
    Task SendOrderConfirmationAsync(int orderId, string email);
}