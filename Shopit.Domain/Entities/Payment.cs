namespace Shopit.Domain.Entities;

public class Payment
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? TransactionRef { get; set; }
    public DateTime? PaidAt { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
}