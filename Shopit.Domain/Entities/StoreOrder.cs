using Shopit.Domain.Enums;

namespace Shopit.Domain.Entities;

public class StoreOrder
{
    public int Id { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal SubTotal { get; set; }
    public decimal CommissionAmount { get; set; }
    public decimal SellerNetAmount { get; set; }

    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public ICollection<StoreOrderItem> StoreOrderItems { get; set; } = new List<StoreOrderItem>();
}
