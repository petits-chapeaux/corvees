using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Corvees.Host.Mcp;

/// <summary>MCP Apps views (extension io.modelcontextprotocol/ui). A view only calls the existing tools through its host.</summary>
public static class AppCatalog
{
    public const string MimeType = "text/html;profile=mcp-app";
    public const string ProjectBoardUri = "ui://corvees/project-board";

    private static readonly string ProjectBoardHtml = Load("project-board.html");

    public static IReadOnlyList<Resource> Resources { get; } = Array.AsReadOnly(
    [
        new Resource
        {
            Uri = ProjectBoardUri,
            Name = "project_board",
            Title = "Projects",
            Description = "Projects with their status and next step, or one project with its steps and dependencies.",
            MimeType = MimeType
        }
    ]);

    /// <summary>Tools whose results are rendered by a view.</summary>
    public static string? ViewFor(string toolName) => toolName is "list_projects" or "get_project" ? ProjectBoardUri : null;

    public static JsonObject ToolMeta(string resourceUri) => new()
    {
        ["ui"] = new JsonObject { ["resourceUri"] = resourceUri },
        ["ui/resourceUri"] = resourceUri
    };

    public static ValueTask<ListResourcesResult> ListAsync(RequestContext<ListResourcesRequestParams> _, CancellationToken __) =>
        ValueTask.FromResult(new ListResourcesResult { Resources = Resources.ToList() });

    public static ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken _)
    {
        if (request.Params?.Uri != ProjectBoardUri)
            throw new McpProtocolException($"Resource not found: {request.Params?.Uri}", McpErrorCode.ResourceNotFound);
        return ValueTask.FromResult(new ReadResourceResult
        {
            Contents =
            [
                new TextResourceContents
                {
                    Uri = ProjectBoardUri,
                    MimeType = MimeType,
                    Text = ProjectBoardHtml,
                    // No csp domains: the view is self-contained and reaches data only through the host's tools/call.
                    Meta = new JsonObject { ["ui"] = new JsonObject { ["prefersBorder"] = true } }
                }
            ]
        });
    }

    private static string Load(string name)
    {
        using var stream = typeof(AppCatalog).Assembly.GetManifestResourceStream("Corvees.Host.Mcp.Apps." + name)
            ?? throw new InvalidOperationException($"Missing embedded MCP App view {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
