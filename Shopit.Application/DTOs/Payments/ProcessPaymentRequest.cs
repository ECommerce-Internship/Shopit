using Shopit.Domain.Enums;

namespace Shopit.Application.DTOs.Payments;

public class ProcessPaymentRequest
{
    public int OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
}