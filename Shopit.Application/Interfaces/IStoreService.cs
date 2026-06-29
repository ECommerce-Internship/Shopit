using Shopit.Application.DTOs.Stores;

namespace Shopit.Application.Interfaces;

public interface IStoreService
{
    /// <summary>Creates a new store (Status = Pending) owned by the given seller.</summary>
    Task<StoreResponse> CreateStoreAsync(int ownerUserId, CreateStoreRequest request);

    /// <summary>Lists the stores owned by the given seller.</summary>
    Task<IReadOnlyList<StoreResponse>> GetMyStoresAsync(int ownerUserId);

    /// <summary>Public storefront lookup: returns the store only if it is Approved, else NotFound.</summary>
    Task<StoreResponse> GetStoreBySlugAsync(string slug);

    /// <summary>Lists all stores awaiting approval (admin).</summary>
    Task<IReadOnlyList<StoreResponse>> GetPendingStoresAsync();

    /// <summary>Approves a store (Pending or Suspended -> Approved).</summary>
    Task<StoreResponse> ApproveStoreAsync(int storeId);

    /// <summary>Rejects a pending store (Pending -> Rejected).</summary>
    Task<StoreResponse> RejectStoreAsync(int storeId);

    /// <summary>Suspends an approved store (Approved -> Suspended).</summary>
    Task<StoreResponse> SuspendStoreAsync(int storeId);
}
