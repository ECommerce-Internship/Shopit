namespace Shopit.Application.Interfaces;

public interface IEmailService
{
    Task SendOrderConfirmationAsync(int orderId, string email);
     Task SendLowStockAlertAsync(string adminEmail, string productName, int currentQty, int threshold);
}