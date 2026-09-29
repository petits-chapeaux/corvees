namespace Corvees.Host;

public static class ApiEndpoints
{
    public static void MapApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet("", () => TypedResults.Ok(new { name = "corvees", version = "v1" }));
    }
}
