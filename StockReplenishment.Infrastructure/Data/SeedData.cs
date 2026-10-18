using Microsoft.EntityFrameworkCore;
using StockReplenishment.Domain;

namespace StockReplenishment.Infrastructure.Data;

/// <summary>Three locations and 15 requests, three in each workflow status.</summary>
public static class SeedData
{
    private const int RequestCount = 15;

    private static readonly (string ArticleNumber, string Description)[] Articles =
    [
        ("BOLT-M8", "M8 steel bolts"),
        ("WASHER-M8", "M8 washers"),
        ("NUT-M8", "M8 hex nuts"),
        ("TIE-200", "Cable ties 200 mm"),
        ("GLOVE-L", "Nitrile gloves, size L"),
        ("TAPE-50", "Packing tape 50 mm"),
        ("LABEL-A6", "Shipping labels A6")
    ];

    public static async Task InitializeAsync(StockDbContext db)
    {
        if (await db.Locations.AnyAsync())
            return;

        db.Locations.AddRange(
            new StockLocation { Id = 1, Name = "Assembly line A" },
            new StockLocation { Id = 2, Name = "Assembly line B" },
            new StockLocation { Id = 3, Name = "Packing station" });

        var statuses = Enum.GetValues<RequestStatus>();
        var priorities = Enum.GetValues<Priority>();

        for (var i = 1; i <= RequestCount; i++)
        {
            var status = statuses[(i - 1) % statuses.Length];
            var priority = priorities[i % priorities.Length];
            var locationId = (i - 1) % 3 + 1;
            var createdAt = DateTimeOffset.UtcNow.AddHours(-i);

            db.Requests.Add(CreateRequest(i, status, priority, locationId, createdAt));
        }

        await db.SaveChangesAsync();
    }

    private static ReplenishmentRequest CreateRequest(
        int number, RequestStatus status, Priority priority, int locationId, DateTimeOffset createdAt)
    {
        var isDraft = status == RequestStatus.Draft;
        var isReviewed = status is RequestStatus.Approved or RequestStatus.Rejected or RequestStatus.Fulfilled;
        var isFulfilled = status == RequestStatus.Fulfilled;

        return new ReplenishmentRequest
        {
            LocationId = locationId,
            Priority = priority,
            Status = status,
            CreatedAt = createdAt,
            SubmittedAt = isDraft ? null : createdAt.AddMinutes(5),
            ReviewedAt = isReviewed ? createdAt.AddMinutes(10) : null,
            FulfilledAt = isFulfilled ? createdAt.AddMinutes(15) : null,
            ValidationStatus = isDraft ? ValidationStatus.NotStarted : ValidationStatus.Available,
            ValidationMessage = isDraft ? null : "All requested materials are available.",
            RejectionReason = status == RequestStatus.Rejected ? "Duplicate of an existing replenishment request." : null,
            // One to three different articles per request, with varying quantities.
            Items = Enumerable.Range(0, number % 3 + 1)
                .Select(line => CreateItem(Articles[(number + line * 2) % Articles.Length], 5 * (number % 4 + line + 1), isDraft, isFulfilled))
                .ToList()
        };
    }

    private static RequestItem CreateItem(
        (string ArticleNumber, string Description) article, decimal quantity, bool isDraft, bool isFulfilled) => new()
        {
            ArticleNumber = article.ArticleNumber,
            Description = article.Description,
            RequestedQuantity = quantity,
            AvailableQuantity = isDraft ? null : 100,
            FulfilledQuantity = isFulfilled ? quantity : null
        };
}
