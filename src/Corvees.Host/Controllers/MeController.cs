using Corvees.Host.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

[Route("api/v1/me")]
public sealed class MeController(BusinessService service) : ResourceController(service)
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => ExecuteAsync(Operation.GetMe, ct);

    [HttpPatch]
    public Task<IActionResult> Update(CancellationToken ct) => ExecuteAsync(Operation.UpdateMe, ct);
}
