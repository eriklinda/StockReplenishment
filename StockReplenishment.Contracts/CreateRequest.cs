using StockReplenishment.Domain;

namespace StockReplenishment.Contracts;

public record CreateRequest(int LocationId, Priority Priority, List<CreateItem> Items);
