using StockReplenishment.Contracts;
using StockReplenishment.Domain;

namespace StockReplenishment.Application;

/// <summary>
/// Owns the replenishment workflow: Draft -> Submitted -> Approved -> Fulfilled, or Submitted -> Rejected.
/// All input validation lives here, so the rules are the same for the API and for direct callers.
/// </summary>
public class RequestService(IRequestRepository repository, RequestLock requestLock)
{
    public const int MaxPage = 10_000;
    public const int MaxPageSize = 100;
    public const int MaxConcurrentValidations = 4;

    public Task<List<LocationResponse>> GetLocations(CancellationToken ct = default) => WithLock(async () =>
    {
        var locations = await repository.GetLocationsAsync(ct);
        return locations.Select(location => new LocationResponse(location.Id, location.Name)).ToList();
    }, ct);

    public Task<RequestResponse> Get(int id, CancellationToken ct = default) => WithLock(async () =>
    {
        var request = await FindAsync(id, ct);
        return request.ToResponse();
    }, ct);

    public Task<RequestPage> List(
        RequestStatus? status, Priority? priority, int? locationId, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || page > MaxPage || pageSize < 1 || pageSize > MaxPageSize)
            throw Validation($"Page must be between 1 and {MaxPage} and pageSize between 1 and {MaxPageSize}.");

        if ((status is { } s && !Enum.IsDefined(s)) || (priority is { } p && !Enum.IsDefined(p)))
            throw Validation("Unknown status or priority filter.");

        return WithLock(async () =>
        {
            var (requests, totalCount) = await repository.ListAsync(status, priority, locationId, page, pageSize, ct);
            var items = requests.Select(request => request.ToResponse()).ToList();
            return new RequestPage(items, totalCount, page, pageSize);
        }, ct);
    }

    public Task<RequestResponse> Create(CreateRequest input, CancellationToken ct = default) => WithLock(async () =>
    {
        if (input.Items is null || input.Items.Count == 0 || input.Items.Count > RequestLimits.MaxItems)
            throw Validation($"Provide between 1 and {RequestLimits.MaxItems} line items.");

        if (input.Items.Any(item => item is null))
            throw Validation("Line items cannot be null.");

        if (input.Items.Any(item => string.IsNullOrWhiteSpace(item.ArticleNumber) || string.IsNullOrWhiteSpace(item.Description)))
            throw Validation("Each line item needs an article number and description.");

        if (input.Items.Any(item => item.ArticleNumber.Trim().Length > RequestLimits.MaxArticleNumberLength
            || item.Description.Trim().Length > RequestLimits.MaxDescriptionLength))
            throw Validation($"Article numbers are limited to {RequestLimits.MaxArticleNumberLength} characters and descriptions to {RequestLimits.MaxDescriptionLength}.");

        if (input.Items.Any(item => item.RequestedQuantity < RequestLimits.MinRequestedQuantity
            || item.RequestedQuantity > RequestLimits.MaxRequestedQuantity))
            throw Validation($"Requested quantities must be between {RequestLimits.MinRequestedQuantity} and {RequestLimits.MaxRequestedQuantity}.");

        if (!Enum.IsDefined(input.Priority))
            throw Validation("Unknown priority.");

        var articleNumbers = input.Items.Select(item => item.ArticleNumber.Trim());
        if (articleNumbers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Items.Count)
            throw Validation("Combine duplicate article numbers into one line item.");

        if (!await repository.LocationExistsAsync(input.LocationId, ct))
            throw Validation("Unknown stock location.");

        var request = new ReplenishmentRequest
        {
            LocationId = input.LocationId,
            Priority = input.Priority,
            Status = RequestStatus.Draft,
            Items = input.Items.Select(item => new RequestItem
            {
                ArticleNumber = item.ArticleNumber.Trim(),
                Description = item.Description.Trim(),
                RequestedQuantity = item.RequestedQuantity
            }).ToList()
        };

        repository.Add(request);
        await repository.SaveChangesAsync(ct);

        // Reload so the response includes the location name.
        var created = await FindAsync(request.Id, ct);
        return created.ToResponse();
    }, ct);

    public Task<RequestResponse> Submit(int id, CancellationToken ct = default) => Update(id, request =>
    {
        if (request.Status != RequestStatus.Draft)
            throw Conflict("Only draft requests can be submitted.");

        request.Status = RequestStatus.Submitted;
        request.SubmittedAt = DateTimeOffset.UtcNow;
        QueueValidation(request, "Stock check queued. You can continue using the app.");
    }, ct);

    public Task<RequestResponse> RetryValidation(int id, CancellationToken ct = default) => Update(id, request =>
    {
        if (request.Status != RequestStatus.Submitted || request.ValidationStatus != ValidationStatus.Failed)
            throw Conflict("Only failed stock checks on submitted requests can be retried.");

        QueueValidation(request, "Stock check queued again.");
    }, ct);

    public Task<RequestResponse> Approve(int id, CancellationToken ct = default) => Update(id, request =>
    {
        if (request.Status != RequestStatus.Submitted)
            throw Conflict("Only submitted requests can be approved.");

        if (request.ValidationStatus != ValidationStatus.Available)
            throw Conflict("Approval requires a successful stock check with all materials available.");

        request.Status = RequestStatus.Approved;
        request.ReviewedAt = DateTimeOffset.UtcNow;
    }, ct);

    public Task<RequestResponse> Reject(int id, string reason, CancellationToken ct = default) => Update(id, request =>
    {
        if (request.Status != RequestStatus.Submitted)
            throw Conflict("Only submitted requests can be rejected.");

        if (string.IsNullOrWhiteSpace(reason))
            throw Validation("A rejection reason is required.");

        if (reason.Trim().Length > RequestLimits.MaxRejectionReasonLength)
            throw Validation($"The rejection reason is limited to {RequestLimits.MaxRejectionReasonLength} characters.");

        request.Status = RequestStatus.Rejected;
        request.RejectionReason = reason.Trim();
        request.ReviewedAt = DateTimeOffset.UtcNow;
    }, ct);

    public Task<RequestResponse> Fulfill(int id, FulfillRequest input, CancellationToken ct = default) => Update(id, request =>
    {
        if (request.Status != RequestStatus.Approved)
            throw Conflict("Only approved requests can be fulfilled.");

        var quantities = GetFulfilledQuantities(request, input);

        // Everything is validated before any item is changed, so a bad request never partially updates.
        foreach (var item in request.Items)
            item.FulfilledQuantity = quantities[item.Id];

        request.Status = RequestStatus.Fulfilled;
        request.FulfilledAt = DateTimeOffset.UtcNow;
    }, ct);

    /// <summary>Requests waiting for the background stock check, oldest submission first.</summary>
    public Task<List<RequestResponse>> GetPendingValidations(CancellationToken ct) => WithLock(async () =>
    {
        var pending = await repository.GetPendingAsync(MaxConcurrentValidations, ct);
        return pending.Select(request => request.ToResponse()).ToList();
    }, ct);

    /// <summary>
    /// Stores the stock check result. <paramref name="availableQuantities"/> is null when the external service failed.
    /// The lifecycle status is never changed here, so a request rejected during the check stays rejected.
    /// </summary>
    public Task<RequestResponse> CompleteValidation(
        int id, IReadOnlyDictionary<int, decimal>? availableQuantities, CancellationToken ct) => Update(id, request =>
    {
        if (request.ValidationStatus != ValidationStatus.Pending)
            return;

        var hasResultForEveryItem = availableQuantities is not null
            && request.Items.All(item => availableQuantities.TryGetValue(item.Id, out var quantity) && quantity >= 0);

        if (!hasResultForEveryItem)
        {
            request.ValidationStatus = ValidationStatus.Failed;
            request.ValidationMessage = "The external stock service failed. Retry the check.";
            return;
        }

        foreach (var item in request.Items)
            item.AvailableQuantity = availableQuantities![item.Id];

        if (request.Items.All(item => item.AvailableQuantity >= item.RequestedQuantity))
        {
            request.ValidationStatus = ValidationStatus.Available;
            request.ValidationMessage = "All requested materials are available.";
        }
        else
        {
            request.ValidationStatus = ValidationStatus.Unavailable;
            request.ValidationMessage = "Insufficient stock. Review the available quantities before rejecting this request.";
        }
    }, ct);

    private static void QueueValidation(ReplenishmentRequest request, string message)
    {
        request.ValidationStatus = ValidationStatus.Pending;
        request.ValidationMessage = message;
    }

    private static Dictionary<int, decimal> GetFulfilledQuantities(ReplenishmentRequest request, FulfillRequest input)
    {
        if (input.Items is null || input.Items.Any(item => item is null))
            throw Validation("Provide fulfilled quantities for the request items.");

        var requestedById = request.Items.ToDictionary(item => item.Id, item => item.RequestedQuantity);
        var inputIds = input.Items.Select(item => item.ItemId).ToHashSet();

        if (input.Items.Count != requestedById.Count || !inputIds.SetEquals(requestedById.Keys))
            throw Validation("Provide each request item exactly once, without unknown item IDs.");

        foreach (var item in input.Items)
        {
            if (item.Quantity < 0 || item.Quantity > requestedById[item.ItemId])
                throw Validation("Fulfilled quantities must be between zero and the requested amount.");
        }

        if (input.Items.All(item => item.Quantity == 0))
            throw Validation("At least one item must have a positive fulfilled quantity.");

        return input.Items.ToDictionary(item => item.ItemId, item => item.Quantity);
    }

    private Task<RequestResponse> Update(int id, Action<ReplenishmentRequest> change, CancellationToken ct) => WithLock(async () =>
    {
        var request = await FindAsync(id, ct);
        change(request);
        await repository.SaveChangesAsync(ct);
        return request.ToResponse();
    }, ct);

    private async Task<ReplenishmentRequest> FindAsync(int id, CancellationToken ct)
    {
        return await repository.GetAsync(id, ct)
            ?? throw new RequestException(RequestError.NotFound, "Request not found.");
    }

    private async Task<T> WithLock<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await requestLock.Gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            requestLock.Gate.Release();
        }
    }

    private static RequestException Validation(string message) => new(RequestError.Validation, message);

    private static RequestException Conflict(string message) => new(RequestError.Conflict, message);
}
