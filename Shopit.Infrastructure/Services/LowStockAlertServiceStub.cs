using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

public class LowStockAlertServiceStub : ILowStockAlertService
{
    public Task SendAlertAsync(int productId, string productName, int currentQty, int threshold)
    {
        return Task.CompletedTask;
    }
}