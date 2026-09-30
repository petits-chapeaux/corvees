using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Corvees.Host.Hosting;

public static class OperationalEndpoints
{
    public static void MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/healthz", () => Results.Ok());
        endpoints.MapGet("/readyz", async (CorveesDbContext db, CancellationToken ct) =>
            await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(503));
        endpoints.MapGet("/api/v1", () => Results.Ok(new { name = "corvees", version = "v1" }));
    }
}
