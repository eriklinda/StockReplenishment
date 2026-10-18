using StockReplenishment.Application;
using StockReplenishment.Contracts;

namespace StockReplenishment.Infrastructure;

/// <summary>Stand-in for the slow external stock service.</summary>
public class SimulatedStockAvailability : IStockAvailability
{
    private const decimal DefaultStock = 100m;
    private const decimal LowStock = 2m;

    public async Task<IReadOnlyDictionary<int, decimal>> CheckAsync(RequestResponse request, CancellationToken ct)
    {
        await Task.Delay(Random.Shared.Next(3000, 7001), ct);

        // Fixed quantities make both the available and the insufficient-stock scenarios reproducible.
        return request.Items.ToDictionary(
            item => item.Id,
            item => IsLowStockArticle(item.ArticleNumber) ? LowStock : DefaultStock);
    }

    private static bool IsLowStockArticle(string articleNumber) =>
        articleNumber.StartsWith("LOW-", StringComparison.OrdinalIgnoreCase);
}
