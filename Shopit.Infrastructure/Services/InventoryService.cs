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
            .Include(i => i.Product).ThenInclude(p => p.Store)
            .Where(i => !i.Product.IsDeleted)
            .OrderBy(i => i.Quantity)
            .Select(i => MapToResponse(i))
            .ToListAsync();
    }

    public async Task<InventoryResponse> GetByProductIdAsync(int productId, int userId, bool isAdmin)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product).ThenInclude(p => p.Store)
            .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        await EnsureOwnsProductAsync(productId, userId, isAdmin);

        return MapToResponse(inventory);
    }

   public async Task<InventoryResponse> UpdateStockAsync(int productId, int quantity, int userId, bool isAdmin)
{
    var inventory = await _context.Inventories
        .Include(i => i.Product).ThenInclude(p => p.Store)
        .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

    if (inventory == null)
        throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

    await EnsureOwnsProductAsync(productId, userId, isAdmin);

    inventory.Quantity = quantity;
    inventory.UpdatedAt = DateTime.UtcNow;
    await _context.SaveChangesAsync();

    try
    {
        if (quantity <= inventory.LowStockThreshold)
        {
            Log.Warning("Low stock triggered for {ProductName} - Qty: {Qty}, Threshold: {Threshold}",
                inventory.Product.Name, quantity, inventory.LowStockThreshold);
            await _lowStockAlertService.SendAlertAsync(
                inventory.ProductId,
                inventory.Product.Name,
                quantity,
                inventory.LowStockThreshold);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed in low stock check for product {ProductId}", productId);
    }

    return MapToResponse(inventory);
}

    public async Task<IEnumerable<InventoryResponse>> GetLowStockAsync()
    {
        return await _context.Inventories
            .Include(i => i.Product).ThenInclude(p => p.Store)
            .Where(i => !i.Product.IsDeleted && i.Quantity <= i.LowStockThreshold)
            .OrderBy(i => i.Quantity)
            .Select(i => MapToResponse(i))
            .ToListAsync();
    }

    public async Task<InventoryResponse> UpdateThresholdAsync(int productId, int threshold, int userId, bool isAdmin)
    {
        var inventory = await _context.Inventories
            .Include(i => i.Product).ThenInclude(p => p.Store)
            .FirstOrDefaultAsync(i => i.ProductId == productId && !i.Product.IsDeleted);

        if (inventory == null)
            throw new NotFoundException($"Inventory for product with ID {productId} was not found.");

        await EnsureOwnsProductAsync(productId, userId, isAdmin);

        inventory.LowStockThreshold = threshold;
        inventory.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToResponse(inventory);
    }

    // A seller may manage inventory only for products in a store they own; an admin bypasses.
    private async Task EnsureOwnsProductAsync(int productId, int userId, bool isAdmin)
    {
        if (isAdmin) return;

        var owns = await _context.Products.AnyAsync(p => p.Id == productId && p.Store.OwnerUserId == userId);
        if (!owns)
            throw new ForbiddenException("You can only manage inventory for products in your own stores.");
    }

    private static InventoryResponse MapToResponse(Inventory inventory) => new()
    {
        ProductId = inventory.ProductId,
        ProductName = inventory.Product.Name,
        SKU = inventory.Product.SKU,
        Quantity = inventory.Quantity,
        LowStockThreshold = inventory.LowStockThreshold,
        IsLowStock = inventory.Quantity <= inventory.LowStockThreshold,
        StoreId = inventory.Product.StoreId,
        StoreName = inventory.Product.Store?.Name ?? string.Empty,
        LastUpdated = inventory.UpdatedAt
    };
}