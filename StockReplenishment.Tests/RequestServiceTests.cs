using NSubstitute;
using StockReplenishment.Application;
using StockReplenishment.Contracts;
using StockReplenishment.Domain;

namespace StockReplenishment.Tests;

public class RequestServiceTests
{
    [TestCaseSource(nameof(InvalidCreateInputs))]
    public async Task Create_InvalidBusinessInput_DoesNotAddOrCommit(CreateRequest input)
    {
        var repository = Substitute.For<IRequestRepository>();
        repository.LocationExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        using var gate = new RequestLock();
        var service = new RequestService(repository, gate);

        var error = Assert.ThrowsAsync<RequestException>(() => service.Create(input));

        Assert.That(error!.Error, Is.EqualTo(RequestError.Validation));
        repository.DidNotReceive().Add(Arg.Any<ReplenishmentRequest>());
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static IEnumerable<TestCaseData> InvalidCreateInputs()
    {
        yield return new TestCaseData(new CreateRequest(1, Priority.Normal, [])).SetName("Create_RejectsEmptyItems");
        yield return new TestCaseData(new CreateRequest(1, Priority.Normal, null!)).SetName("Create_RejectsNullItems");
        yield return new TestCaseData(new CreateRequest(1, Priority.Normal, [null!])).SetName("Create_RejectsNullItem");
        yield return new TestCaseData(new CreateRequest(1, Priority.Normal,
            Enumerable.Range(0, RequestLimits.MaxItems + 1).Select(i => new CreateItem($"A-{i}", "Material", 1)).ToList()))
            .SetName("Create_RejectsTooManyItems");

        foreach (var quantity in new[] { -1m, 0m, 0.0001m, RequestLimits.MaxRequestedQuantity + 1 })
            yield return new TestCaseData(new CreateRequest(1, Priority.Normal, [new("A", "Material", quantity)]))
                .SetName($"Create_RejectsQuantity_{quantity}");

        yield return new TestCaseData(new CreateRequest(1, Priority.Normal, [new(null!, "Material", 1)]))
            .SetName("Create_RejectsNullArticleNumber");
        yield return new TestCaseData(new CreateRequest(1, Priority.Normal, [new(" ", "Material", 1)]))
            .SetName("Create_RejectsBlankArticleNumber");
        yield return new TestCaseData(new CreateRequest(1, Priority.Normal, [new("A", " ", 1)]))
            .SetName("Create_RejectsBlankDescription");
        yield return new TestCaseData(new CreateRequest(1, (Priority)999, [new("A", "Material", 1)]))
            .SetName("Create_RejectsUnknownPriority");
    }

    [TestCase("0.001")]
    [TestCase("999999999")]
    public async Task Create_QuantityAtBoundary_IsAccepted(string quantityText)
    {
        var quantity = decimal.Parse(quantityText, System.Globalization.CultureInfo.InvariantCulture);
        var repository = Substitute.For<IRequestRepository>();
        repository.LocationExistsAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(Request(RequestStatus.Draft, ValidationStatus.NotStarted));
        repository.When(x => x.Add(Arg.Any<ReplenishmentRequest>()))
            .Do(call => call.Arg<ReplenishmentRequest>().Id = 1);
        using var gate = new RequestLock();
        var service = new RequestService(repository, gate);

        await service.Create(new CreateRequest(1, Priority.Normal, [new("A", "Material", quantity)]));

        repository.Received(1).Add(Arg.Is<ReplenishmentRequest>(x => x.Items.Single().RequestedQuantity == quantity));
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Approval_WithPendingStock_DoesNotCommitChanges()
    {
        var repository = Substitute.For<IRequestRepository>();
        var request = Request(RequestStatus.Submitted, ValidationStatus.Pending);
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(request);
        using var gate = new RequestLock();
        var service = new RequestService(repository, gate);

        var error = Assert.ThrowsAsync<RequestException>(() => service.Approve(1));

        Assert.That(error!.Error, Is.EqualTo(RequestError.Conflict));
        Assert.That(request.Status, Is.EqualTo(RequestStatus.Submitted));
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Approval_WithAvailableStock_CommitsReviewedRequest()
    {
        var repository = Substitute.For<IRequestRepository>();
        var request = Request(RequestStatus.Submitted, ValidationStatus.Available);
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(request);
        using var gate = new RequestLock();
        var service = new RequestService(repository, gate);

        var response = await service.Approve(1);

        Assert.That(response.Status, Is.EqualTo(RequestStatus.Approved));
        Assert.That(response.ReviewedAt, Is.Not.Null);
        await repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidFulfillment_DoesNotMutateEarlierItemsOrCommit()
    {
        var repository = Substitute.For<IRequestRepository>();
        var request = Request(RequestStatus.Approved, ValidationStatus.Available);
        repository.GetAsync(1, Arg.Any<CancellationToken>()).Returns(request);
        using var gate = new RequestLock();
        var service = new RequestService(repository, gate);

        var input = new FulfillRequest([new FulfillItem(1, 5), new FulfillItem(2, 999)]);

        Assert.ThrowsAsync<RequestException>(() => service.Fulfill(1, input));

        Assert.That(request.Items.All(x => x.FulfilledQuantity is null), Is.True);
        Assert.That(request.Status, Is.EqualTo(RequestStatus.Approved));
        await repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static ReplenishmentRequest Request(RequestStatus status, ValidationStatus validationStatus) => new()
    {
        Id = 1,
        LocationId = 1,
        Location = new StockLocation { Id = 1, Name = "Line A" },
        Status = status,
        ValidationStatus = validationStatus,
        Items =
        [
            new RequestItem { Id = 1, ArticleNumber = "A", RequestedQuantity = 10 },
            new RequestItem { Id = 2, ArticleNumber = "B", RequestedQuantity = 20 }
        ]
    };
}
