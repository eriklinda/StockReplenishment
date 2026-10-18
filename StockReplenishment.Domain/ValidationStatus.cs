namespace StockReplenishment.Domain;

/// <summary>Result of the external stock availability check, tracked separately from the workflow status.</summary>
public enum ValidationStatus
{
    NotStarted,
    Pending,
    Available,
    Unavailable,
    Failed
}
