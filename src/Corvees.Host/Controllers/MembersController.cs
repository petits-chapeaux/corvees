using Corvees.Host.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

[Route("api/v1/members")]
public sealed class MembersController(BusinessService service) : ResourceController(service)
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken ct) => ExecuteAsync(Operation.ListMembers, ct);
}
