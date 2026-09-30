using Corvees.Host.Contracts;

namespace Corvees.Host.Mcp;

internal static class OutputSchemas
{
    private static readonly object Uuid = new { type = "string", format = "uuid" };
    private static readonly object Text = new { type = "string" };
    private static readonly object NullableText = new { type = new[] { "string", "null" } };
    private static readonly object DateTime = new { type = "string", format = "date-time" };
    private static readonly object NullableDateTime = new { type = new[] { "string", "null" }, format = "date-time" };
    private static readonly object Version = new { type = "integer", minimum = 1 };
    private static readonly object Dependencies = new { type = "array", items = Uuid };

    public static object For(OperationDefinition operation)
    {
        var resource = Resource(operation.Resource);
        var properties = new Dictionary<string, object>
        {
            ["schemaVersion"] = new { type = "integer", @const = 1 },
            ["kind"] = new { type = "string", @const = operation.Name },
            ["data"] = operation.IsList ? new { type = "array", items = resource } : resource
        };
        if (operation.IsList)
            properties["nextCursor"] = NullableText;
        return Object(properties);
    }

    private static object Resource(ResourceKind resource)
    {
        var properties = new Dictionary<string, object>
        {
            ["id"] = Uuid,
            ["version"] = Version,
            ["createdAt"] = DateTime,
            ["updatedAt"] = DateTime
        };
        switch (resource)
        {
            case ResourceKind.Group:
                properties["name"] = Text;
                break;
            case ResourceKind.Member:
                properties["groupId"] = Uuid;
                properties["displayName"] = Text;
                break;
            case ResourceKind.Location:
                properties["name"] = Text;
                properties["address"] = NullableText;
                break;
            case ResourceKind.Project:
                properties["title"] = Text;
                properties["description"] = NullableText;
                properties["locationId"] = new { type = new[] { "string", "null" }, format = "uuid" };
                properties["location"] = new
                {
                    type = new[] { "object", "null" },
                    properties = new { id = Uuid, available = new { type = "boolean" }, name = NullableText },
                    required = new[] { "id", "available", "name" }
                };
                properties["status"] = new { type = "string", @enum = new[] { "planned", "active", "complete", "archived" } };
                properties["dependencies"] = Dependencies;
                properties["nextStep"] = new
                {
                    type = new[] { "object", "null" },
                    properties = new { id = Uuid, title = Text, status = new { type = "string", @enum = new[] { "todo", "in_progress" } } },
                    required = new[] { "id", "title", "status" }
                };
                properties["stepListVersion"] = Version;
                properties["archivedAt"] = NullableDateTime;
                break;
            case ResourceKind.Step:
                properties["projectId"] = Uuid;
                properties["title"] = Text;
                properties["description"] = NullableText;
                properties["status"] = new { type = "string", @enum = new[] { "todo", "in_progress", "done" } };
                properties["position"] = Version;
                properties["dependencies"] = Dependencies;
                break;
            default:
                throw new InvalidOperationException($"No output schema for {resource}");
        }
        if (resource != ResourceKind.Group)
            properties["deletedAt"] = NullableDateTime;
        return Object(properties);
    }

    private static object Object(Dictionary<string, object> properties) =>
        new { type = "object", properties, required = properties.Keys.ToArray() };
}
