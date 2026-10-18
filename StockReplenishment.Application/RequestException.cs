namespace StockReplenishment.Application;

public sealed class RequestException(RequestError error, string message) : Exception(message)
{
    public RequestError Error { get; } = error;
}
