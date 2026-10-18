using StockReplenishment.Contracts;
using StockReplenishment.Domain;

namespace StockReplenishment.Application;

internal static class RequestMapping
{
    public static RequestResponse ToResponse(this ReplenishmentRequest request)
    {
        var items = request.Items
            .OrderBy(item => item.Id)
            .Select(item => item.ToResponse())
            .ToList();

        return new RequestResponse(
            request.Id,
            request.LocationId,
            request.Location.Name,
            request.Priority,
            request.Status,
            request.ValidationStatus,
            request.ValidationMessage,
            request.RejectionReason,
            request.CreatedAt,
            request.SubmittedAt,
            request.ReviewedAt,
            request.FulfilledAt,
            items);
    }

    private static ItemResponse ToResponse(this RequestItem item) => new(
        item.Id,
        item.ArticleNumber,
        item.Description,
        item.RequestedQuantity,
        item.AvailableQuantity,
        item.FulfilledQuantity);
}
