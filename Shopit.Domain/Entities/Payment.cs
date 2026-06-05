using Shopit.Domain.Enums;

namespace Shopit.Domain.Entities;

public class Payment
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; } = PaymentMethod.Card;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? TransactionRef { get; set; }
    public DateTime? PaidAt { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}