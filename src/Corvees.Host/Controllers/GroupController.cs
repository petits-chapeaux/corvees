using Corvees.Host.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

[Route("api/v1/group")]
public sealed class GroupController(BusinessService service) : ResourceController(service)
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => ExecuteAsync(Operation.GetGroup, ct);

    [HttpPatch]
    public Task<IActionResult> Update(CancellationToken ct) => ExecuteAsync(Operation.UpdateGroup, ct);
}
