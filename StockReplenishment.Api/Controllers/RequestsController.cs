using Microsoft.AspNetCore.Mvc;
using StockReplenishment.Api.Filters;
using StockReplenishment.Application;
using StockReplenishment.Contracts;
using StockReplenishment.Domain;

namespace StockReplenishment.Api.Controllers;

[ApiController]
[Route("api/requests")]
[RequestExceptionFilter]
public class RequestsController(RequestService service) : ControllerBase
{
    [HttpGet]
    public Task<RequestPage> List(
        [FromQuery] RequestStatus? status,
        [FromQuery] Priority? priority,
        [FromQuery] int? locationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        return service.List(status, priority, locationId, page, pageSize, ct);
    }

    [HttpGet("{id:int}")]
    public Task<RequestResponse> Get(int id, CancellationToken ct)
    {
        return service.Get(id, ct);
    }

    [HttpPost]
    public async Task<ActionResult<RequestResponse>> Create(CreateRequest input, CancellationToken ct)
    {
        var created = await service.Create(input, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Submits the request and queues the slow stock check. Poll the returned location for the result.</summary>
    [HttpPost("{id:int}/submit")]
    public async Task<ActionResult<RequestResponse>> Submit(int id, CancellationToken ct)
    {
        var submitted = await service.Submit(id, ct);
        return AcceptedAtAction(nameof(Get), new { id }, submitted);
    }

    [HttpPost("{id:int}/retry-validation")]
    public async Task<ActionResult<RequestResponse>> RetryValidation(int id, CancellationToken ct)
    {
        var queued = await service.RetryValidation(id, ct);
        return AcceptedAtAction(nameof(Get), new { id }, queued);
    }

    [HttpPost("{id:int}/approve")]
    public Task<RequestResponse> Approve(int id, CancellationToken ct)
    {
        return service.Approve(id, ct);
    }

    [HttpPost("{id:int}/reject")]
    public Task<RequestResponse> Reject(int id, RejectRequest input, CancellationToken ct)
    {
        return service.Reject(id, input.Reason, ct);
    }

    [HttpPost("{id:int}/fulfill")]
    public Task<RequestResponse> Fulfill(int id, FulfillRequest input, CancellationToken ct)
    {
        return service.Fulfill(id, input, ct);
    }
}
