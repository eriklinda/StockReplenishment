namespace StockReplenishment.Contracts;

public record ItemResponse(
    int Id,
    string ArticleNumber,
    string Description,
    decimal RequestedQuantity,
    decimal? AvailableQuantity,
    decimal? FulfilledQuantity);
