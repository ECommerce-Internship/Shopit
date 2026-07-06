namespace Shopit.Application.Interfaces;

public interface ILowStockAlertService
{
    Task SendAlertAsync(int productId, string productName, int currentQty, int threshold);
}