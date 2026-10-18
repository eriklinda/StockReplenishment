using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using StockReplenishment.Application;
using StockReplenishment.Contracts;
using StockReplenishment.Domain;

namespace StockReplenishment.Tests;

/// <summary>
/// Runs the real API (MVC pipeline, EF InMemory and the background worker) with a substituted stock service.
/// The substitute waits until <see cref="releaseStock"/> is completed, so tests control when the check finishes.
/// </summary>
public class WorkflowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private WebApplicationFactory<Program> app = null!;
    private HttpClient client = null!;
    private IStockAvailability stock = null!;
    private TaskCompletionSource releaseStock = null!;

    [SetUp]
    public void SetUp()
    {
        releaseStock = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        stock = Substitute.For<IStockAvailability>();
        stock.CheckAsync(Arg.Any<RequestResponse>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await releaseStock.Task.WaitAsync(call.ArgAt<CancellationToken>(1));
            return AllAvailable(call.Arg<RequestResponse>());
        });

        app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("DatabaseName", Guid.NewGuid().ToString())
            .ConfigureServices(services =>
            {
                services.RemoveAll<IStockAvailability>();
                services.AddSingleton(stock);
            }));

        client = app.CreateClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        client.Dispose();
        await app.DisposeAsync();
    }

    [Test]
    public async Task CompleteWorkflow_SubmitsImmediately_ThenApprovesAndFulfills()
    {
        var created = await Create();
        Assert.That(created.Status, Is.EqualTo(RequestStatus.Draft));

        var watch = Stopwatch.StartNew();
        var submitResponse = await Post(created.Id, "submit");

        Assert.That(submitResponse.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        Assert.That((await Read(submitResponse)).ValidationStatus, Is.EqualTo(ValidationStatus.Pending));
        Assert.That((await Post(created.Id, "approve")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        releaseStock.SetResult();
        var validated = await WaitForValidation(created.Id, ValidationStatus.Available);

        Assert.That(validated.Items[0].AvailableQuantity, Is.EqualTo(100));
        await stock.Received(1).CheckAsync(Arg.Is<RequestResponse>(x => x.Id == created.Id), Arg.Any<CancellationToken>());

        var approved = await Read(await Post(created.Id, "approve"));
        Assert.That(approved.Status, Is.EqualTo(RequestStatus.Approved));

        var fulfillment = new FulfillRequest([new FulfillItem(created.Items[0].Id, 7)]);
        var fulfilled = await Read(await Post(created.Id, "fulfill", fulfillment));

        Assert.Multiple(() =>
        {
            Assert.That(fulfilled.Status, Is.EqualTo(RequestStatus.Fulfilled));
            Assert.That(fulfilled.Items[0].FulfilledQuantity, Is.EqualTo(7));
            Assert.That(fulfilled.SubmittedAt, Is.Not.Null);
            Assert.That(fulfilled.ReviewedAt, Is.Not.Null);
            Assert.That(fulfilled.FulfilledAt, Is.Not.Null);
        });
        Assert.That((await Post(created.Id, "approve")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task ConcurrentSubmissions_OnlyOneSucceeds()
    {
        var created = await Create();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(created.Id, "submit")));

        Assert.That(responses.Count(x => x.StatusCode == HttpStatusCode.Accepted), Is.EqualTo(1));
        Assert.That(responses.Count(x => x.StatusCode == HttpStatusCode.Conflict), Is.EqualTo(4));
    }

    [Test]
    public async Task InsufficientStock_BlocksApproval_ButAllowsRejection()
    {
        var created = await Create(quantity: 101);
        await Post(created.Id, "submit");

        releaseStock.SetResult();
        await WaitForValidation(created.Id, ValidationStatus.Unavailable);

        Assert.That((await Post(created.Id, "approve")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var rejected = await Read(await Post(created.Id, "reject", new RejectRequest("Insufficient material")));

        Assert.That(rejected.Status, Is.EqualTo(RequestStatus.Rejected));
        Assert.That(rejected.RejectionReason, Is.EqualTo("Insufficient material"));
    }

    [Test]
    public async Task ExternalServiceFailure_CanBeRetried()
    {
        stock.CheckAsync(Arg.Any<RequestResponse>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyDictionary<int, decimal>>(new HttpRequestException("Unavailable")));

        var created = await Create();
        await Post(created.Id, "submit");
        await WaitForValidation(created.Id, ValidationStatus.Failed);

        stock.CheckAsync(Arg.Any<RequestResponse>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(AllAvailable(call.Arg<RequestResponse>())));

        Assert.That((await Post(created.Id, "retry-validation")).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        await WaitForValidation(created.Id, ValidationStatus.Available);
    }

    [Test]
    public async Task Rejection_RequiresReason_AndIsNotOverwrittenByBackgroundCheck()
    {
        var created = await Create();
        await Post(created.Id, "submit");

        Assert.That((await Post(created.Id, "reject", new RejectRequest(" "))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await Read(await Post(created.Id, "reject", new RejectRequest("Duplicate")));

        releaseStock.SetResult();
        await WaitForValidation(created.Id, ValidationStatus.Available);

        Assert.That((await Get(created.Id)).Status, Is.EqualTo(RequestStatus.Rejected));
    }

    [TestCase("""{"locationId":999,"priority":"Normal","items":[{"articleNumber":"A","description":"B","requestedQuantity":1}]}""")]
    [TestCase("""{"locationId":1,"priority":"Normal","items":[]}""")]
    [TestCase("""{"locationId":1,"priority":"Unknown","items":[]}""")]
    [TestCase("""{"locationId":1,"priority":"Normal","items":[null]}""")]
    [TestCase("""{"locationId":1,"priority":"Normal","items":[{"articleNumber":"A","description":"B","requestedQuantity":0}]}""")]
    [TestCase("""{"locationId":1,"priority":"Normal","items":[{"articleNumber":"A","description":"B","requestedQuantity":1},{"articleNumber":"a ","description":"C","requestedQuantity":1}]}""")]
    public async Task InvalidCreate_ReturnsProblemDetails(string json)
    {
        var response = await client.PostAsync("/api/requests", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task Fulfillment_ValidatesAllItemsBeforeChangingAnything()
    {
        var approvedPage = await client.GetFromJsonAsync<RequestPage>("/api/requests?status=Approved", Json);
        var request = approvedPage!.Items.First(x => x.Items.Count > 1);
        var firstItemId = request.Items[0].Id;

        List<List<FulfillItem>> invalidInputs =
        [
            [new(firstItemId, 1)],
            [new(firstItemId, 1), new(firstItemId, 1)],
            [new(firstItemId, 1), new(999999, 1)],
            request.Items.Select(x => new FulfillItem(x.Id, x.RequestedQuantity + 1)).ToList(),
            request.Items.Select(x => new FulfillItem(x.Id, -1)).ToList(),
            request.Items.Select(x => new FulfillItem(x.Id, 0)).ToList()
        ];

        foreach (var items in invalidInputs)
        {
            var response = await Post(request.Id, "fulfill", new FulfillRequest(items));
            var unchanged = await Get(request.Id);

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(unchanged.Status, Is.EqualTo(RequestStatus.Approved));
            Assert.That(unchanged.Items.All(x => x.FulfilledQuantity is null), Is.True);
        }
    }

    [Test]
    public async Task List_FiltersAndPaginates()
    {
        var filtered = await client.GetFromJsonAsync<RequestPage>("/api/requests?status=Draft&priority=Normal&locationId=1&pageSize=1", Json);

        Assert.That(filtered!.Items, Has.Count.EqualTo(1));
        Assert.That(filtered.Items[0].Status, Is.EqualTo(RequestStatus.Draft));
        Assert.That(filtered.Items[0].Priority, Is.EqualTo(Priority.Normal));
        Assert.That(filtered.Items[0].LocationId, Is.EqualTo(1));

        var secondPage = await client.GetFromJsonAsync<RequestPage>("/api/requests?page=2&pageSize=10", Json);

        Assert.That(secondPage!.Items, Has.Count.EqualTo(5));
        Assert.That(secondPage.TotalCount, Is.EqualTo(15));
    }

    [TestCase("/api/requests?page=0", HttpStatusCode.BadRequest)]
    [TestCase("/api/requests?pageSize=101", HttpStatusCode.BadRequest)]
    [TestCase("/api/requests?page=10001", HttpStatusCode.BadRequest)]
    [TestCase("/api/requests?status=999", HttpStatusCode.BadRequest)]
    [TestCase("/api/requests/999999", HttpStatusCode.NotFound)]
    public async Task InvalidReads_ReturnExpectedStatus(string url, HttpStatusCode expected)
    {
        var response = await client.GetAsync(url);

        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [TestCase("approve")]
    [TestCase("fulfill")]
    [TestCase("reject")]
    [TestCase("retry-validation")]
    public async Task Draft_CannotSkipSubmission(string action)
    {
        var created = await Create();
        var body = new { reason = "Test", items = new[] { new { itemId = created.Items[0].Id, quantity = 1 } } };

        var response = await Post(created.Id, action, body);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    private static IReadOnlyDictionary<int, decimal> AllAvailable(RequestResponse request) =>
        request.Items.ToDictionary(x => x.Id, _ => 100m);

    private async Task<RequestResponse> Create(decimal quantity = 10)
    {
        var input = new CreateRequest(1, Priority.Urgent, [new CreateItem("TEST-001", "Test material", quantity)]);
        using var response = await client.PostAsJsonAsync("/api/requests", input, Json);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        Assert.That(response.Headers.Location?.ToString(), Does.Contain("/api/requests/"));
        return await Read(response);
    }

    private Task<HttpResponseMessage> Post(int id, string action, object? body = null) =>
        body is null
            ? client.PostAsync($"/api/requests/{id}/{action}", null)
            : client.PostAsJsonAsync($"/api/requests/{id}/{action}", body, Json);

    private async Task<RequestResponse> Get(int id) => await Read(await client.GetAsync($"/api/requests/{id}"));

    private static async Task<RequestResponse> Read(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RequestResponse>(Json))!;
    }

    private async Task<RequestResponse> WaitForValidation(int id, ValidationStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            var request = await Get(id);
            if (request.ValidationStatus == expected)
                return request;

            await Task.Delay(50, timeout.Token);
        }
    }
}
