using Shopit.Application.DTOs;

namespace Shopit.Application.Interfaces;

public interface IOrderService
{
    Task<OrderResponse> PlaceOrderAsync(int userId, PlaceOrderRequest request);
}