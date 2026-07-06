using Shopit.Domain.Enums;

namespace Shopit.Application.DTOs.Payments;

public class PaymentResponse
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; }
    public PaymentMethod Method { get; set; }
    public string? TransactionRef { get; set; }
    public DateTime? PaidAt { get; set; }
}