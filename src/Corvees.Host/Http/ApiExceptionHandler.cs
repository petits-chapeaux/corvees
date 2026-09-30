using Corvees.Host.Application;
using Microsoft.AspNetCore.Diagnostics;

namespace Corvees.Host.Http;

public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var error = exception as DomainException;
        context.Response.StatusCode = error?.Status ?? StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            code = error?.Code ?? "internal_error",
            message = error?.Message ?? "Unexpected server error"
        }, ct);
        return true;
    }
}
