using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;
using Serilog;

namespace Shopit.Infrastructure.Services;

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _context;
    private readonly ILowStockAlertService _lowStockAlertService;

    public InventoryService(AppDbContext context, ILowStockAlertService lowStockAlertService)
    {
        _context = context;
        _lowStockAlertService = lowStockAlertService;
    }

    public async Task<IEnumerable<InventoryResponse>> GetAllAsync()
    {
        return await _context.Inventories
            .Include(i => i.Product)
            .Where(i => !i.Product.IsDeleted)
            .OrderBy(i => i.Quantity)
            .Select(i => MapToResponse(i))
            .ToListAsync();
    }

    public async Task<InventoryResponse> GetByProductIdAsync(int productId)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        return MapToResponse(inventory);
    }

    public async Task<InventoryResponse> UpdateStockAsync(int productId, int quantity)
{
    var inventory = await _context.Inventories
        .Include(i => i.Product)
        .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

    if (inventory == null)
        throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

    inventory.Quantity = quantity;
    inventory.UpdatedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();
   try
    {
        Console.WriteLine($"=== CHECKING LOW STOCK: quantity={quantity}, threshold={inventory.LowStockThreshold} ===");

        if (quantity <= inventory.LowStockThreshold)
{
    Console.WriteLine($"=== LOW STOCK TRIGGERED for {inventory.Product.Name} ===");
    await _lowStockAlertService.SendAlertAsync(
        inventory.ProductId,
        inventory.Product.Name,
        quantity,
        inventory.LowStockThreshold);
}
      
    }
    catch (Exception ex)
    {
        Log.Error(ex, "FAILED in low stock check: {Message}", ex.Message);
    }

return MapToResponse(inventory);
}

    public async Task<IEnumerable<InventoryResponse>> GetLowStockAsync()
    {
        return await _context.Inventories
            .Include(i => i.Product)
            .Where(i => !i.Product.IsDeleted && i.Quantity <= i.LowStockThreshold)
            .OrderBy(i => i.Quantity)
            .Select(i => MapToResponse(i))
            .ToListAsync();
    }

    public async Task<InventoryResponse> UpdateThresholdAsync(int productId, int threshold)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product)
            .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        inventory.LowStockThreshold = threshold;
        inventory.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToResponse(inventory);
    }

    private static InventoryResponse MapToResponse(Inventory inventory) => new()
    {
        ProductId = inventory.ProductId,
        ProductName = inventory.Product.Name,
        SKU = inventory.Product.SKU,
        Quantity = inventory.Quantity,
        LowStockThreshold = inventory.LowStockThreshold,
        IsLowStock = inventory.Quantity <= inventory.LowStockThreshold,
        LastUpdated = inventory.UpdatedAt
    };
}