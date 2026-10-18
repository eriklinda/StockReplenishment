namespace StockReplenishment.Contracts;

public record RequestPage(List<RequestResponse> Items, int TotalCount, int Page, int PageSize);
