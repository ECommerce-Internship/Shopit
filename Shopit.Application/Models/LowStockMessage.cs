namespace Shopit.Application.Models;

public class LowStockMessage
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int CurrentQty { get; set; }
    public int Threshold { get; set; }
}