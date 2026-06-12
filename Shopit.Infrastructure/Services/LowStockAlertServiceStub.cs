using Shopit.Application.Interfaces;

namespace Shopit.Infrastructure.Services;

public class LowStockAlertServiceStub : ILowStockAlertService
{
    public Task TriggerAlertAsync(int productId)
    {
        // TODO: SCRUM-47 - implement real alert logic
        return Task.CompletedTask;
    }
}