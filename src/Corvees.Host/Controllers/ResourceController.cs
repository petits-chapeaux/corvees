using Corvees.Host.Contracts;
using Corvees.Host.Http;
using Microsoft.AspNetCore.Mvc;

namespace Corvees.Host.Controllers;

public abstract class ResourceController(BusinessService service) : ControllerBase
{
    protected async Task<IActionResult> ExecuteAsync(Operation operation, CancellationToken ct)
    {
        var arguments = await ApiRequestReader.ReadAsync(Request, OperationCatalog.Get(operation), ct);
        var result = await service.ExecuteAsync(operation, arguments, ct);
        if (result.Version is { } version)
            Response.Headers.ETag = $"\"{version}\"";
        return new JsonResult(result);
    }
}
