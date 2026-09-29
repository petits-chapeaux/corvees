using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Corvees.Host;

public static class ToolCatalog
{
    private static readonly Dictionary<string, string> Fields = new()
    {
        ["projectId"] = "uuid", ["stepId"] = "uuid", ["locationId"] = "nullable_uuid", ["prerequisiteId"] = "uuid",
        ["targetStepId"] = "uuid", ["title"] = "string", ["name"] = "string", ["displayName"] = "string",
        ["description"] = "nullable", ["address"] = "nullable", ["status"] = "status", ["placement"] = "placement",
        ["expectedVersion"] = "integer", ["expectedListVersion"] = "integer", ["limit"] = "limit",
        ["cursor"] = "string", ["deletedOnly"] = "boolean", ["includeDeleted"] = "boolean"
    };

    private static readonly string[] Page = ["limit", "cursor", "deletedOnly"];
    private static string[] Join(params string[][] parts) => parts.SelectMany(x => x).ToArray();

    private sealed record Spec(string Name, string[] Required, string[] Optional, bool ReadOnly = false, bool Destructive = false, bool Idempotent = false);
    private static readonly Spec[] Specs =
    [
        new("get_group", [], [], ReadOnly: true),
        new("update_group", ["name", "expectedVersion"], []),
        new("list_members", [], ["limit", "cursor"], ReadOnly: true),
        new("get_me", [], [], ReadOnly: true),
        new("update_me", ["displayName", "expectedVersion"], []),
        new("list_locations", [], Page, ReadOnly: true),
        new("get_location", ["locationId"], ["includeDeleted"], ReadOnly: true),
        new("create_location", ["name"], ["address"]),
        new("update_location", ["locationId", "expectedVersion"], ["name", "address"]),
        new("delete_location", ["locationId", "expectedVersion"], [], Destructive: true),
        new("restore_location", ["locationId", "expectedVersion"], []),
        new("list_projects", [], Join(Page, ["status", "locationId"]), ReadOnly: true),
        new("get_project", ["projectId"], ["includeDeleted"], ReadOnly: true),
        new("create_project", ["title"], ["description", "locationId"]),
        new("update_project", ["projectId", "expectedVersion"], ["title", "description", "locationId"]),
        new("archive_project", ["projectId", "expectedVersion"], []),
        new("unarchive_project", ["projectId", "expectedVersion"], []),
        new("delete_project", ["projectId", "expectedVersion"], [], Destructive: true),
        new("restore_project", ["projectId", "expectedVersion"], []),
        new("list_steps", ["projectId"], Page, ReadOnly: true),
        new("get_step", ["projectId", "stepId"], ["includeDeleted"], ReadOnly: true),
        new("create_step", ["projectId", "title", "expectedListVersion"], ["description"]),
        new("update_step", ["projectId", "stepId", "expectedVersion"], ["title", "description", "status"]),
        new("move_step", ["projectId", "stepId", "targetStepId", "placement", "expectedListVersion"], []),
        new("delete_step", ["projectId", "stepId", "expectedVersion", "expectedListVersion"], [], Destructive: true),
        new("restore_step", ["projectId", "stepId", "expectedVersion", "expectedListVersion"], []),
        new("add_project_dependency", ["projectId", "prerequisiteId", "expectedVersion"], []),
        new("remove_project_dependency", ["projectId", "prerequisiteId", "expectedVersion"], [], Destructive: true),
        new("add_step_dependency", ["projectId", "stepId", "prerequisiteId", "expectedVersion"], []),
        new("remove_step_dependency", ["projectId", "stepId", "prerequisiteId", "expectedVersion"], [], Destructive: true)
    ];

    private static object SchemaField(string field, string tool) => Fields[field] switch
    {
        "uuid" => new { type = "string", format = "uuid" },
        "nullable_uuid" => tool is "get_location" or "update_location" or "delete_location" or "restore_location"
            ? new { type = new[] { "string" }, format = "uuid" }
            : new { type = new[] { "string", "null" }, format = "uuid" },
        "integer" => new { type = "integer", minimum = 1 },
        "limit" => new { type = "integer", minimum = 1, maximum = 100 },
        "boolean" => new { type = "boolean" },
        "nullable" => new { type = new[] { "string", "null" }, maxLength = field == "address" ? 500 : 10000 },
        "status" => new { type = "string", @enum = tool == "list_projects"
            ? ["planned", "active", "complete", "archived"] : new[] { "todo", "in_progress", "done" } },
        "placement" => new { type = "string", @enum = new[] { "before", "after" } },
        _ => new { type = "string", minLength = 1, maxLength = field == "cursor" ? 4096 : 200 }
    };

    private static object ResourceSchema(string name)
    {
        var resource = name.Contains("step_dependency") || name.EndsWith("_step") || name == "list_steps" ? "step"
            : name.Contains("project_dependency") || name.EndsWith("_project") || name == "list_projects" ? "project"
            : name.EndsWith("_location") || name == "list_locations" ? "location"
            : name is "get_me" or "update_me" or "list_members" ? "member" : "group";
        var fields = new Dictionary<string, object>
        {
            ["id"] = new { type = "string", format = "uuid" },
            ["version"] = new { type = "integer", minimum = 1 },
            ["createdAt"] = new { type = "string", format = "date-time" },
            ["updatedAt"] = new { type = "string", format = "date-time" }
        };
        if (resource == "group") fields["name"] = new { type = "string" };
        if (resource == "member")
        {
            fields["groupId"] = new { type = "string", format = "uuid" };
            fields["displayName"] = new { type = "string" };
        }
        if (resource == "location")
        {
            fields["name"] = new { type = "string" };
            fields["address"] = new { type = new[] { "string", "null" } };
        }
        if (resource == "project")
        {
            fields["title"] = new { type = "string" };
            fields["description"] = new { type = new[] { "string", "null" } };
            fields["locationId"] = new { type = new[] { "string", "null" } };
            fields["location"] = new { type = new[] { "object", "null" } };
            fields["status"] = new { type = "string", @enum = new[] { "planned", "active", "complete", "archived" } };
            fields["dependencies"] = new { type = "array", items = new { type = "string", format = "uuid" } };
            fields["stepListVersion"] = new { type = "integer", minimum = 1 };
            fields["archivedAt"] = new { type = new[] { "string", "null" } };
        }
        if (resource == "step")
        {
            fields["projectId"] = new { type = "string", format = "uuid" };
            fields["title"] = new { type = "string" };
            fields["description"] = new { type = new[] { "string", "null" } };
            fields["status"] = new { type = "string", @enum = new[] { "todo", "in_progress", "done" } };
            fields["position"] = new { type = "integer", minimum = 1 };
            fields["dependencies"] = new { type = "array", items = new { type = "string", format = "uuid" } };
        }
        if (resource != "group") fields["deletedAt"] = new { type = new[] { "string", "null" } };
        return new { type = "object", properties = fields, required = fields.Keys.ToArray() };
    }

    private static object OutputSchema(Spec spec)
    {
        var list = spec.Name.StartsWith("list_");
        var fields = new Dictionary<string, object>
        {
            ["schemaVersion"] = new { type = "integer", @const = 1 },
            ["kind"] = new { type = "string", @const = spec.Name },
            ["data"] = list ? new { type = "array", items = ResourceSchema(spec.Name) } : ResourceSchema(spec.Name)
        };
        if (list) fields["nextCursor"] = new { type = new[] { "string", "null" } };
        return new { type = "object", properties = fields, required = fields.Keys.ToArray() };
    }

    public static IReadOnlyList<Tool> Tools { get; } = Specs.Select(spec => new Tool
    {
        Name = spec.Name,
        Description = spec.Name.Replace('_', ' ') + " in the authenticated member's group. UUIDs are stable; writes require current versions.",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object", properties = spec.Required.Concat(spec.Optional).Distinct().ToDictionary(x => x, x => SchemaField(x, spec.Name)),
            required = spec.Required, additionalProperties = false
        }),
        OutputSchema = JsonSerializer.SerializeToElement(OutputSchema(spec)),
        Annotations = new ToolAnnotations { ReadOnlyHint = spec.ReadOnly, DestructiveHint = spec.Destructive,
            IdempotentHint = spec.Idempotent, OpenWorldHint = false }
    }).ToArray();

    public static bool Contains(string name) => Specs.Any(x => x.Name == name);
}
