using Microsoft.EntityFrameworkCore;
using StockReplenishment.Application;
using StockReplenishment.Domain;

namespace StockReplenishment.Infrastructure.Data;

public sealed class EfRequestRepository(StockDbContext db) : IRequestRepository
{
    private IQueryable<ReplenishmentRequest> Requests => db.Requests.Include(x => x.Location).Include(x => x.Items);

    public Task<List<StockLocation>> GetLocationsAsync(CancellationToken ct) =>
        db.Locations.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public Task<bool> LocationExistsAsync(int id, CancellationToken ct) =>
        db.Locations.AnyAsync(x => x.Id == id, ct);

    public Task<ReplenishmentRequest?> GetAsync(int id, CancellationToken ct) =>
        Requests.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<(List<ReplenishmentRequest> Items, int TotalCount)> ListAsync(
        RequestStatus? status, Priority? priority, int? locationId, int page, int pageSize, CancellationToken ct)
    {
        var query = Requests.AsNoTracking();
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (priority.HasValue) query = query.Where(x => x.Priority == priority);
        if (locationId.HasValue) query = query.Where(x => x.LocationId == locationId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<List<ReplenishmentRequest>> GetPendingAsync(int limit, CancellationToken ct) =>
        Requests.AsNoTracking().Where(x => x.ValidationStatus == ValidationStatus.Pending)
            .OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id).Take(limit).ToListAsync(ct);

    public void Add(ReplenishmentRequest request) => db.Requests.Add(request);

    public async Task SaveChangesAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
