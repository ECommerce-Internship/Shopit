using Shopit.Domain.Enums;

namespace Shopit.Application.DTOs.Payments;

public class PaymentQueryParameters
{
    public PaymentStatus? Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}