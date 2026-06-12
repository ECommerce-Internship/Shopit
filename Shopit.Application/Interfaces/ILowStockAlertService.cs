namespace Shopit.Application.Interfaces;

public interface ILowStockAlertService
{
    Task TriggerAlertAsync(int productId);
}