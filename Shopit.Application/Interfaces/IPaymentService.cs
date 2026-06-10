using Shopit.Application.DTOs.Payments;

namespace Shopit.Application.Interfaces;

public interface IPaymentService
{
    Task<PaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, int currentUserId);
    Task<PaymentResponse> GetByOrderIdAsync(int orderId, int currentUserId);
    Task<PaymentResponse> RefundAsync(int paymentId);
    Task<List<PaymentResponse>> GetAllPaymentsAsync(PaymentQueryParameters parameters);
}