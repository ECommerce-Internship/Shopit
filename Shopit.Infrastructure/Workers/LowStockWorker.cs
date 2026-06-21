using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Shopit.Application.Interfaces;
using Shopit.Application.Models;

namespace Shopit.Infrastructure.Workers;

public class LowStockWorker : BackgroundService
{
    private readonly QueueClient _queueClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _adminEmail;

    public LowStockWorker(QueueClient queueClient, IServiceProvider serviceProvider, 
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _queueClient = queueClient;
        _serviceProvider = serviceProvider;
        _adminEmail = configuration["SendGrid:AdminEmail"]!;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("=== LowStockWorker STARTED ===");

        await _queueClient.CreateIfNotExistsAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine($"=== WORKER CHECKING QUEUE at {DateTime.Now} ===");
            Console.WriteLine($"=== WORKER reading from queue: {_queueClient.Name} ===");
            try
            {
                var response = await _queueClient.ReceiveMessagesAsync(maxMessages: 10, cancellationToken: stoppingToken);
                Console.WriteLine($"=== WORKER found {response.Value.Length} messages ===");
                foreach (var message in response.Value)
                {
                    Console.WriteLine($"=== PROCESSING message: {message.Body} ===");
                    try
                    {
                        var lowStockMessage = JsonSerializer.Deserialize<LowStockMessage>(message.Body.ToString());

                        if (lowStockMessage is not null)
                        {
                            using var scope = _serviceProvider.CreateScope();
                            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                            await emailService.SendLowStockAlertAsync(
                                _adminEmail,
                                lowStockMessage.ProductName,
                                lowStockMessage.CurrentQty,
                                lowStockMessage.Threshold);

                           Console.WriteLine($"=== Email sent, deleting message ===");
                            await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, stoppingToken);
                            Console.WriteLine($"=== Message deleted ===");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to process low stock message");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"=== ERROR: {ex.Message} ===");
                Console.WriteLine($"=== STACK: {ex.StackTrace} ===");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        Log.Information("LowStockWorker stopped");
    }
}