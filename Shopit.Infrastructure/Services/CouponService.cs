using Microsoft.EntityFrameworkCore;
using Shopit.Application.DTOs.Coupons;
using Shopit.Application.Interfaces;
using Shopit.Domain.Entities;
using Shopit.Domain.Exceptions;
using Shopit.Infrastructure.Data;

namespace Shopit.Infrastructure.Services;

public class CouponService : ICouponService
{
    private readonly AppDbContext _context;

    public CouponService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CouponResponse> CreateAsync(int userId, bool isAdmin, CreateCouponRequest request)
    {
        var code = request.Code.Trim();

        // Business rule: ownership. A seller may only create coupons for a store they own;
        // Admins may create platform-wide coupons (StoreId null) or target any store.
        await EnsureStoreAccessAsync(request.StoreId, userId, isAdmin);

        // Business rule: unique code (index-backed, but checked here for a friendly 409).
        var codeTaken = await _context.Coupons.AnyAsync(c => c.Code.ToLower() == code.ToLower());
        if (codeTaken)
            throw new ConflictException($"A coupon with code '{code}' already exists.");

        var coupon = new Coupon
        {
            Code = code,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            MinimumOrderAmount = request.MinimumOrderAmount,
            UsageLimit = request.UsageLimit,
            UsageCount = 0,
            ExpiresAt = request.ExpiresAt,
            IsActive = true,
            StoreId = request.StoreId
        };

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();

        return MapToResponse(coupon);
    }

    public async Task<IReadOnlyList<CouponResponse>> GetAllAsync(int userId, bool isAdmin)
    {
        var query = _context.Coupons.AsNoTracking();

        // Sellers only ever see coupons scoped to a store they own.
        if (!isAdmin)
            query = query.Where(c => c.StoreId != null && c.Store!.OwnerUserId == userId);

        var coupons = await query
            .OrderByDescending(c => c.Id)
            .ToListAsync();

        return coupons.Select(MapToResponse).ToList();
    }

    public async Task<CouponResponse> GetByIdAsync(int id, int userId, bool isAdmin)
    {
        var coupon = await FindOwnedCouponAsync(id, userId, isAdmin);
        return MapToResponse(coupon);
    }

    public async Task<CouponResponse> UpdateAsync(int id, int userId, bool isAdmin, UpdateCouponRequest request)
    {
        var coupon = await FindOwnedCouponAsync(id, userId, isAdmin);

        // Business rule: a usage limit can never drop below what has already been redeemed.
        if (request.UsageLimit.HasValue && request.UsageLimit.Value < coupon.UsageCount)
            throw new ValidationException(
                $"Usage limit ({request.UsageLimit}) cannot be lower than the current usage count ({coupon.UsageCount}).");

        coupon.DiscountType = request.DiscountType;
        coupon.DiscountValue = request.DiscountValue;
        coupon.MinimumOrderAmount = request.MinimumOrderAmount;
        coupon.UsageLimit = request.UsageLimit;
        coupon.ExpiresAt = request.ExpiresAt;

        await _context.SaveChangesAsync();

        return MapToResponse(coupon);
    }

    public async Task<CouponResponse> DeactivateAsync(int id, int userId, bool isAdmin)
    {
        var coupon = await FindOwnedCouponAsync(id, userId, isAdmin);

        coupon.IsActive = false;
        await _context.SaveChangesAsync();

        return MapToResponse(coupon);
    }

    /// <summary>
    /// Loads a coupon and enforces that the caller is allowed to manage it.
    /// Admins may manage any coupon; sellers only coupons scoped to a store they own.
    /// </summary>
    private async Task<Coupon> FindOwnedCouponAsync(int id, int userId, bool isAdmin)
    {
        var coupon = await _context.Coupons
            .Include(c => c.Store)
            .FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException($"Coupon with ID {id} was not found.");

        if (!isAdmin && (coupon.StoreId == null || coupon.Store!.OwnerUserId != userId))
            throw new ForbiddenException("You can only manage coupons for your own store.");

        return coupon;
    }

    /// <summary>
    /// Validates the target store for a create. Admins may target null (platform-wide) or any
    /// existing store; sellers must target a store they own.
    /// </summary>
    private async Task EnsureStoreAccessAsync(int? storeId, int userId, bool isAdmin)
    {
        if (!isAdmin && storeId == null)
            throw new ForbiddenException("Sellers must scope a coupon to a store they own.");

        if (storeId == null)
            return;

        var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == storeId.Value)
            ?? throw new NotFoundException($"Store with ID {storeId} was not found.");

        if (!isAdmin && store.OwnerUserId != userId)
            throw new ForbiddenException("You can only create coupons for your own store.");
    }

    private static CouponResponse MapToResponse(Coupon coupon) => new()
    {
        Id = coupon.Id,
        Code = coupon.Code,
        DiscountType = coupon.DiscountType.ToString(),
        DiscountValue = coupon.DiscountValue,
        MinimumOrderAmount = coupon.MinimumOrderAmount,
        UsageLimit = coupon.UsageLimit,
        UsageCount = coupon.UsageCount,
        ExpiresAt = coupon.ExpiresAt,
        IsActive = coupon.IsActive,
        StoreId = coupon.StoreId
    };
}
