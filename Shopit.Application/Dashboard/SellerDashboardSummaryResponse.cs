namespace Shopit.Application.DTOs.Dashboard;

public class SellerDashboardSummaryResponse
{
    public decimal GrossSales { get; set; }       // Σ SubTotal (status != Cancelled)
    public decimal TotalCommission { get; set; }  // Σ CommissionAmount (status != Cancelled)
    public decimal NetEarnings { get; set; }      // Σ SellerNetAmount (status != Cancelled)
    public int TotalOrders { get; set; }          // count of the seller's StoreOrders (all statuses)
    public int LowStockCount { get; set; }        // seller's inventories at/under threshold
    public int TodaysNewOrders { get; set; }      // seller's StoreOrders with Order.CreatedAt today
}
