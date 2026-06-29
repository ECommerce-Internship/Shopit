using Shopit.Application.DTOs;

namespace Shopit.Application.Interfaces;

public interface IInventoryService
{
    Task<IEnumerable<InventoryResponse>> GetAllAsync();
    Task<InventoryResponse> GetByProductIdAsync(int productId, int userId, bool isAdmin);
    Task<InventoryResponse> UpdateStockAsync(int productId, int quantity, int userId, bool isAdmin);
    Task<IEnumerable<InventoryResponse>> GetLowStockAsync();
    Task<InventoryResponse> UpdateThresholdAsync(int productId, int threshold, int userId, bool isAdmin);
}