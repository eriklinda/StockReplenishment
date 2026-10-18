namespace StockReplenishment.Contracts;

public static class RequestLimits
{
    public const int MaxItems = 100;
    public const int MaxArticleNumberLength = 50;
    public const int MaxDescriptionLength = 200;
    public const int MaxRejectionReasonLength = 1000;
    public const decimal MinRequestedQuantity = 0.001m;
    public const decimal MaxRequestedQuantity = 999999999m;
}
