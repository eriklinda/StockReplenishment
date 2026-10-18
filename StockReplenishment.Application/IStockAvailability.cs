using StockReplenishment.Contracts;

namespace StockReplenishment.Application;

public interface IStockAvailability
{
    Task<IReadOnlyDictionary<int, decimal>> CheckAsync(RequestResponse request, CancellationToken ct);
}
