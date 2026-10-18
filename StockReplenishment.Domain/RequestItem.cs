namespace StockReplenishment.Domain;

public class RequestItem
{
    public int Id { get; set; }
    public int ReplenishmentRequestId { get; set; }
    public string ArticleNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal RequestedQuantity { get; set; }
    public decimal? AvailableQuantity { get; set; }
    public decimal? FulfilledQuantity { get; set; }
}
