using System.Text.Json;
using System.Text.Json.Nodes;
using Corvees.Host.Application;
using Corvees.Host.Contracts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Corvees.Host.Mcp;

public static class McpToolHandler
{
    public static async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request, CancellationToken ct)
    {
        try
        {
            if (!OperationCatalog.TryGet(request.Params!.Name, out var operation))
                throw DomainException.NotFound("Tool");
            var arguments = JsonSerializer.SerializeToNode(request.Params.Arguments) as JsonObject ?? new JsonObject();
            var http = request.Server.Services!.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new InvalidOperationException("MCP tools require an HTTP request scope.");
            var service = http.RequestServices.GetRequiredService<BusinessService>();
            var result = await service.ExecuteAsync(operation.Operation, arguments, ct);
            var content = JsonSerializer.SerializeToElement(result, result.GetType(), JsonSerializerOptions.Web);
            List<ContentBlock> blocks = [new TextContentBlock { Text = content.GetRawText() }];
            if (ToolText.Summarize(result) is { } summary)
                blocks.Add(new TextContentBlock { Text = summary });
            return new CallToolResult { StructuredContent = content, Content = blocks };
        }
        catch (DomainException exception)
        {
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { code = exception.Code, message = exception.Message }) }]
            };
        }
    }
}
