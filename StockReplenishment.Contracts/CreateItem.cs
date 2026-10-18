namespace StockReplenishment.Contracts;

public record CreateItem(string ArticleNumber, string Description, decimal RequestedQuantity);
