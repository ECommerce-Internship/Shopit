using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.Configuration;
using Serilog;
using Shopit.Application.Interfaces;
using Shopit.Application.Models;

namespace Shopit.Infrastructure.Services;

public class LowStockAlertService : ILowStockAlertService
{
    private readonly QueueClient _queueClient;

    public LowStockAlertService(QueueClient queueClient)
    {
        _queueClient = queueClient;
    }

  public async Task SendAlertAsync(int productId, string productName, int currentQty, int threshold)
{
    Console.WriteLine($"=== SENDING to queue: {_queueClient.Name} ===");
    Console.WriteLine($"=== ENTERING SendAlertAsync for {productName} ===");
    await _queueClient.CreateIfNotExistsAsync();
    Console.WriteLine($"=== Queue created/verified ===");

    var message = new LowStockMessage {
            ProductId = productId,
            ProductName = productName,
            CurrentQty = currentQty,
            Threshold = threshold
        };

        var json = JsonSerializer.Serialize(message);
        await _queueClient.SendMessageAsync(BinaryData.FromString(json));

        Console.WriteLine($"=== Queue message sent for {productName} ===");
    }
}