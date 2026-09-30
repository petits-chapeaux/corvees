using Corvees.Host.Contracts;

namespace Corvees.Host.Mcp;

internal static class InputSchemas
{
    public static object For(OperationDefinition operation) => new
    {
        type = "object",
        properties = operation.Required.Concat(operation.Optional).ToDictionary(field => field, field => Field(field, operation)),
        required = operation.Required,
        additionalProperties = false
    };

    private static object Field(string field, OperationDefinition operation) => field switch
    {
        "projectId" or "stepId" or "prerequisiteId" or "targetStepId" => new { type = "string", format = "uuid" },
        "locationId" => new { type = operation.Resource == ResourceKind.Location ? new[] { "string" } : ["string", "null"], format = "uuid" },
        "expectedVersion" or "expectedListVersion" => new { type = "integer", minimum = 1 },
        "limit" => new { type = "integer", minimum = 1, maximum = 100 },
        "deletedOnly" or "includeDeleted" => new { type = "boolean" },
        "address" => new { type = new[] { "string", "null" }, maxLength = 500 },
        "description" => new { type = new[] { "string", "null" }, maxLength = 10000 },
        "status" => new
        {
            type = "string",
            @enum = operation.Resource == ResourceKind.Project
            ? new[] { "planned", "active", "complete", "archived" } : ["todo", "in_progress", "done"]
        },
        "placement" => new { type = "string", @enum = new[] { "before", "after" } },
        "cursor" => new { type = "string", minLength = 1, maxLength = 4096 },
        "title" or "name" or "displayName" => new { type = "string", minLength = 1, maxLength = 200 },
        _ => throw new InvalidOperationException($"No input schema for {field}")
    };
}
