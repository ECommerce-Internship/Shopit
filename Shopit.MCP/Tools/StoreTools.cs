using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Shopit.Application.DTOs.Stores;
using Shopit.Application.Interfaces;
using System.ComponentModel;

namespace Shopit.MCP.Tools;

[McpServerToolType]
public class StoreTools
{
    private readonly IStoreService _storeService;

    public StoreTools(IStoreService storeService)
    {
        _storeService = storeService;
    }

    // Seller-only. The owner userId is injected from the caller's JWT by ChatService
    // (identity injection) and hidden from the model, so a seller can only create a
    // store for themselves. New stores start in Pending status and require admin
    // approval before their products are publicly listed.
    [McpServerTool, Description("Creates a new store owned by the seller. The store starts in Pending status and must be approved by an admin before its products are publicly listed.")]
    public async Task<string> create_store(
        [Description("Store name")] string name,
        [Description("ID of the seller who will own the store")] int userId,
        [Description("Optional store description")] string? description = null)
    {
        try
        {
            var request = new CreateStoreRequest
            {
                Name = name,
                Description = description
            };

            var store = await _storeService.CreateStoreAsync(userId, request);
            return JsonSerializer.Serialize(new
            {
                message = $"Store '{store.Name}' created with status {store.Status}. "
                    + "It must be approved by an admin before its products are publicly listed.",
                storeId = store.Id,
                slug = store.Slug,
                status = store.Status
            });
        }
        catch (Exception ex)
        {
            throw new McpException($"Could not create store: {ex.Message}");
        }
    }
}
