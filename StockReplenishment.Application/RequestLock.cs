namespace StockReplenishment.Application;

/// <summary>
/// Serializes database work within this app instance, because the EF Core InMemory provider
/// has no transactions. The slow external stock check runs outside this lock.
/// </summary>
public sealed class RequestLock : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public void Dispose() => Gate.Dispose();
}
