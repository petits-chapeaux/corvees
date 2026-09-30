using System.Text.Json;
using Corvees.Host.Contracts;
using ModelContextProtocol.Protocol;

namespace Corvees.Host.Mcp;

public static class ToolCatalog
{
    public static IReadOnlyList<Tool> Tools { get; } = Array.AsReadOnly(OperationCatalog.Definitions.Select(definition => new Tool
    {
        Name = definition.Name,
        Description = definition.Name.Replace('_', ' ') + " in the authenticated member's group. UUIDs are stable; writes require current versions.",
        InputSchema = JsonSerializer.SerializeToElement(InputSchemas.For(definition)),
        OutputSchema = JsonSerializer.SerializeToElement(OutputSchemas.For(definition)),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = definition.ReadOnly,
            DestructiveHint = definition.Destructive,
            IdempotentHint = false,
            OpenWorldHint = false
        }
    }).ToArray());
}
