using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Corvees.Host;

public static class ApiEndpoints
{
    private static Func<HttpContext, Task<IResult>> Handler(string operation) => context => Run(context, operation);

    public static void MapApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet("", () => TypedResults.Ok(new { name = "corvees", version = "v1" }));
        api.MapGet("/group", Handler("get_group"));
        api.MapPatch("/group", Handler("update_group"));
        api.MapGet("/members", Handler("list_members"));
        api.MapGet("/me", Handler("get_me"));
        api.MapPatch("/me", Handler("update_me"));

        api.MapGet("/locations", Handler("list_locations"));
        api.MapGet("/locations/{locationId:guid}", Handler("get_location"));
        api.MapPost("/locations", Handler("create_location"));
        api.MapPatch("/locations/{locationId:guid}", Handler("update_location"));
        api.MapDelete("/locations/{locationId:guid}", Handler("delete_location"));
        api.MapPost("/locations/{locationId:guid}/restore", Handler("restore_location"));

        api.MapGet("/projects", Handler("list_projects"));
        api.MapGet("/projects/{projectId:guid}", Handler("get_project"));
        api.MapPost("/projects", Handler("create_project"));
        api.MapPatch("/projects/{projectId:guid}", Handler("update_project"));
        api.MapDelete("/projects/{projectId:guid}", Handler("delete_project"));
        api.MapPost("/projects/{projectId:guid}/restore", Handler("restore_project"));
        api.MapPost("/projects/{projectId:guid}/archive", Handler("archive_project"));
        api.MapPost("/projects/{projectId:guid}/unarchive", Handler("unarchive_project"));
        api.MapPost("/projects/{projectId:guid}/dependencies", Handler("add_project_dependency"));
        api.MapDelete("/projects/{projectId:guid}/dependencies/{prerequisiteId:guid}", Handler("remove_project_dependency"));

        api.MapGet("/projects/{projectId:guid}/steps", Handler("list_steps"));
        api.MapGet("/projects/{projectId:guid}/steps/{stepId:guid}", Handler("get_step"));
        api.MapPost("/projects/{projectId:guid}/steps", Handler("create_step"));
        api.MapPatch("/projects/{projectId:guid}/steps/{stepId:guid}", Handler("update_step"));
        api.MapDelete("/projects/{projectId:guid}/steps/{stepId:guid}", Handler("delete_step"));
        api.MapPost("/projects/{projectId:guid}/steps/{stepId:guid}/restore", Handler("restore_step"));
        api.MapPost("/projects/{projectId:guid}/steps/{stepId:guid}/move", Handler("move_step"));
        api.MapPost("/projects/{projectId:guid}/steps/{stepId:guid}/dependencies", Handler("add_step_dependency"));
        api.MapDelete("/projects/{projectId:guid}/steps/{stepId:guid}/dependencies/{prerequisiteId:guid}", Handler("remove_step_dependency"));
    }

    private static async Task<IResult> Run(HttpContext context, string operation)
    {
        try
        {
            var args = context.Request.Method is "POST" or "PATCH" && context.Request.HasJsonContentType()
                ? await context.Request.ReadFromJsonAsync<JsonObject>() ?? new JsonObject()
                : new JsonObject();
            foreach (var value in context.Request.RouteValues)
                if (value.Key is "projectId" or "stepId" or "locationId" or "prerequisiteId")
                    args[value.Key] = value.Value?.ToString();
            foreach (var value in context.Request.Query)
            {
                if (value.Key == "limit" && int.TryParse(value.Value, out var limit)) args["limit"] = limit;
                else if (value.Key is "deletedOnly" or "includeDeleted" && bool.TryParse(value.Value, out var boolean)) args[value.Key] = boolean;
                else if (value.Key is "cursor" or "status" or "locationId") args[value.Key] = value.Value.ToString();
            }
            if (context.Request.Method is "PATCH" or "DELETE" || operation is "archive_project" or "unarchive_project"
                or "restore_project" or "restore_step" or "restore_location" or "move_step"
                or "add_project_dependency" or "remove_project_dependency" or "add_step_dependency"
                or "remove_step_dependency" or "create_step")
            {
                var key = operation is "create_step" or "move_step" ? "expectedListVersion" : "expectedVersion";
                args[key] = ParseVersion(context.Request.Headers.IfMatch.ToString());
                if (operation is "delete_step" or "restore_step")
                    args["expectedListVersion"] = ParseVersion(context.Request.Headers["X-Step-List-Version"].ToString());
            }
            var result = await context.RequestServices.GetRequiredService<BusinessService>()
                .ExecuteAsync(operation, args, context.RequestAborted);
            var node = JsonSerializer.SerializeToNode(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var version = node?["data"] is JsonObject data ? data["version"] : null;
            if (version != null) context.Response.Headers.ETag = $"\"{version}\"";
            return Results.Json(result);
        }
        catch (DomainException e) { return Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.Status); }
        catch (DbUpdateConcurrencyException) { return Results.Json(new { code = "version_conflict", message = "Read the resource again" }, statusCode: 412); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_steps_project_position" })
        { return Results.Json(new { code = "version_conflict", message = "The step list changed" }, statusCode: 412); }
        catch (JsonException) { return Results.Json(new { code = "validation_error", message = "Invalid JSON" }, statusCode: 400); }
    }

    private static long ParseVersion(string value)
    {
        if (!long.TryParse(value.Trim('"'), out var version) || version < 1)
            throw new DomainException("missing_precondition", 428, "A valid If-Match version is required");
        return version;
    }
}
