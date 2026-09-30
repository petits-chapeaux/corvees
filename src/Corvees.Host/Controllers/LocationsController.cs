using Corvees.Host.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

[Route("api/v1/locations")]
public sealed class LocationsController(BusinessService service) : ResourceController(service)
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => ExecuteAsync(Operation.ListLocations, ct);

    [HttpGet("{locationId:guid}")]
    public Task<IActionResult> Get(CancellationToken ct) => ExecuteAsync(Operation.GetLocation, ct);

    [HttpPost]
    public Task<IActionResult> Create(CancellationToken ct) => ExecuteAsync(Operation.CreateLocation, ct);

    [HttpPatch("{locationId:guid}")]
    public Task<IActionResult> Update(CancellationToken ct) => ExecuteAsync(Operation.UpdateLocation, ct);

    [HttpDelete("{locationId:guid}")]
    public Task<IActionResult> Delete(CancellationToken ct) => ExecuteAsync(Operation.DeleteLocation, ct);

    [HttpPost("{locationId:guid}/restore")]
    public Task<IActionResult> Restore(CancellationToken ct) => ExecuteAsync(Operation.RestoreLocation, ct);
}
