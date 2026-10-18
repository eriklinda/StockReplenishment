using StockReplenishment.Domain;

namespace StockReplenishment.Application;

/// <summary>A scoped session for replenishment storage. SaveChanges commits tracked mutations.</summary>
public interface IRequestRepository
{
    Task<List<StockLocation>> GetLocationsAsync(CancellationToken ct);
    Task<bool> LocationExistsAsync(int id, CancellationToken ct);
    Task<ReplenishmentRequest?> GetAsync(int id, CancellationToken ct);
    Task<(List<ReplenishmentRequest> Items, int TotalCount)> ListAsync(
        RequestStatus? status, Priority? priority, int? locationId, int page, int pageSize, CancellationToken ct);
    Task<List<ReplenishmentRequest>> GetPendingAsync(int limit, CancellationToken ct);
    void Add(ReplenishmentRequest request);
    Task SaveChangesAsync(CancellationToken ct);
}
