namespace StockReplenishment.Domain;

public class ReplenishmentRequest
{
    public int Id { get; set; }
    public int LocationId { get; set; }
    public StockLocation Location { get; set; } = null!;
    public Priority Priority { get; set; }
    public RequestStatus Status { get; set; }
    public ValidationStatus ValidationStatus { get; set; }
    public string? ValidationMessage { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public List<RequestItem> Items { get; set; } = [];
}
