using Shopit.Application.DTOs.Coupons;

namespace Shopit.Application.Interfaces;

public interface ICouponService
{
    Task<CouponResponse> CreateAsync(int userId, bool isAdmin, CreateCouponRequest request);
    Task<IReadOnlyList<CouponResponse>> GetAllAsync(int userId, bool isAdmin);
    Task<CouponResponse> GetByIdAsync(int id, int userId, bool isAdmin);
    Task<CouponResponse> UpdateAsync(int id, int userId, bool isAdmin, UpdateCouponRequest request);
    Task<CouponResponse> DeactivateAsync(int id, int userId, bool isAdmin);
}
