using Microsoft.AspNetCore.Mvc;
using StockReplenishment.Application;
using StockReplenishment.Contracts;

namespace StockReplenishment.Api.Controllers;

[ApiController]
[Route("api/locations")]
public class LocationsController(RequestService service) : ControllerBase
{
    [HttpGet]
    public Task<List<LocationResponse>> Get(CancellationToken ct)
    {
        return service.GetLocations(ct);
    }
}
