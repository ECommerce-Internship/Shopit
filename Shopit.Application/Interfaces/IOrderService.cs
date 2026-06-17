using Shopit.Application.DTOs;

namespace Shopit.Application.Interfaces;

public interface IOrderService
{
    Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request);
    Task<PaginatedResponse<OrderSummaryResponse>> GetMyOrdersAsync(int userId, int page, int pageSize);
    Task<OrderResponse> GetOrderByIdAsync(int orderId, int userId, bool isAdmin);
    Task<OrderResponse> CancelOrderAsync(int orderId, int userId);
    Task<PaginatedResponse<OrderSummaryResponse>> GetAllOrdersAsync(int page, int pageSize, string? status, DateTime? from, DateTime? to);
    Task<OrderResponse> UpdateOrderStatusAsync(int orderId, UpdateOrderStatusRequest request);
}