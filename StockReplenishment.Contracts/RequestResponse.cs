using StockReplenishment.Domain;

namespace StockReplenishment.Contracts;

public record RequestResponse(
    int Id,
    int LocationId,
    string Location,
    Priority Priority,
    RequestStatus Status,
    ValidationStatus ValidationStatus,
    string? ValidationMessage,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    DateTimeOffset? FulfilledAt,
    List<ItemResponse> Items);
