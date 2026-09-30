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
            return new CallToolResult
            {
                StructuredContent = content,
                Content = [new TextContentBlock { Text = content.GetRawText() }]
            };
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
