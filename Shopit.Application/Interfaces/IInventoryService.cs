using Shopit.Application.DTOs;

namespace Shopit.Application.Interfaces;

public interface IInventoryService
{
    Task<IEnumerable<InventoryResponse>> GetAllAsync();
    Task<InventoryResponse> GetByProductIdAsync(int productId);
    Task<InventoryResponse> UpdateStockAsync(int productId, int quantity);
    Task<IEnumerable<InventoryResponse>> GetLowStockAsync();
    Task<InventoryResponse> UpdateThresholdAsync(int productId, int threshold);
}