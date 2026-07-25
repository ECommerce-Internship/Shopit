using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Stores;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Enums;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class StoreService : IStoreService
{
    private readonly AppDbContext _context;
    private readonly ICacheService _cache;

    public StoreService(AppDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<StoreResponse> CreateStoreAsync(int ownerUserId, CreateStoreRequest request)
    {
        var ownerExists = await _context.Users.AnyAsync(u => u.Id == ownerUserId);
        if (!ownerExists)
            throw new NotFoundException($"User with ID {ownerUserId} was not found.");

        var store = new Store
        {
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Slug = await GenerateUniqueSlugAsync(request.Name),
            Status = StoreStatus.Pending,
            CommissionRate = 0,
            OwnerUserId = ownerUserId,
            CreatedAt = DateTime.UtcNow
        };

        _context.Stores.Add(store);
        await _context.SaveChangesAsync();

        return MapToResponse(store);
    }

    public async Task<IReadOnlyList<StoreResponse>> GetMyStoresAsync(int ownerUserId)
    {
        var stores = await _context.Stores
            .AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return stores.Select(MapToResponse).ToList();
    }

    public async Task<StoreResponse> GetStoreBySlugAsync(string slug)
    {
        var store = await _context.Stores
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == slug && s.Status == StoreStatus.Approved);

        if (store is null)
            throw new NotFoundException($"Store '{slug}' was not found.");

        return MapToResponse(store);
    }

    public async Task<IReadOnlyList<StoreResponse>> GetPendingStoresAsync()
    {
        var stores = await _context.Stores
            .AsNoTracking()
            .Where(s => s.Status == StoreStatus.Pending)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        return stores.Select(MapToResponse).ToList();
    }

    public Task<StoreResponse> ApproveStoreAsync(int storeId) =>
        TransitionAsync(storeId, StoreStatus.Approved,
            from: new[] { StoreStatus.Pending, StoreStatus.Suspended });

    public Task<StoreResponse> RejectStoreAsync(int storeId) =>
        TransitionAsync(storeId, StoreStatus.Rejected,
            from: new[] { StoreStatus.Pending });

    public Task<StoreResponse> SuspendStoreAsync(int storeId) =>
        TransitionAsync(storeId, StoreStatus.Suspended,
            from: new[] { StoreStatus.Approved });

    private async Task<StoreResponse> TransitionAsync(int storeId, StoreStatus target, StoreStatus[] from)
    {
        var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == storeId)
            ?? throw new NotFoundException($"Store with ID {storeId} was not found.");

        if (store.Status == target)
            throw new ConflictException($"Store {storeId} is already {target}.");

        if (!from.Contains(store.Status))
            throw new ConflictException($"Cannot change store {storeId} from {store.Status} to {target}.");

        store.Status = target;
        await _context.SaveChangesAsync();

        // A store's visibility changed, so the cached public product catalog is now stale.
        await _cache.RemoveByPatternAsync("products:*");

        return MapToResponse(store);
    }

    private async Task<string> GenerateUniqueSlugAsync(string name)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var suffix = 2;

        while (await _context.Stores.AnyAsync(s => s.Slug == slug))
            slug = $"{baseSlug}-{suffix++}";

        return slug;
    }

    private static string Slugify(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (slug.Contains("--"))
            slug = slug.Replace("--", "-");

        slug = slug.Trim('-');

        return string.IsNullOrEmpty(slug) ? "store" : slug;
    }

    private static StoreResponse MapToResponse(Store store) => new()
    {
        Id = store.Id,
        Name = store.Name,
        Slug = store.Slug,
        Description = store.Description,
        Status = store.Status.ToString(),
        CommissionRate = store.CommissionRate,
        OwnerUserId = store.OwnerUserId,
        CreatedAt = store.CreatedAt
    };
public async Task<IReadOnlyList<StoreResponse>> GetAllStoresAsync(string? status = null)
{
    var query = _context.Stores.AsQueryable();

    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<StoreStatus>(status, true, out var storeStatus))
        query = query.Where(s => s.Status == storeStatus);

    var stores = await query.OrderByDescending(s => s.CreatedAt).ToListAsync();
    return stores.Select(MapToResponse).ToList();
}
}
