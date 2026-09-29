using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvees.Host;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<CorveesDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Corvees")));
builder.Services.AddScoped(services =>
    (MemberSession)services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Items["member"]!);
builder.Services.AddScoped<BusinessService>();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = ToolCatalog.Tools.ToList() }))
    .WithCallToolHandler(async (request, cancellationToken) =>
    {
        var http = request.Server.Services!.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        try
        {
            var name = request.Params!.Name;
            if (!ToolCatalog.Contains(name)) throw new DomainException("not_found", 404, "Unknown tool");
            var args = JsonSerializer.SerializeToNode(request.Params.Arguments) as JsonObject ?? new JsonObject();
            var result = await http.RequestServices.GetRequiredService<BusinessService>().ExecuteAsync(name, args, cancellationToken);
            var content = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return new CallToolResult
            {
                StructuredContent = content,
                Content = [new TextContentBlock { Text = content.GetRawText() }]
            };
        }
        catch (DomainException e)
        {
            return new CallToolResult { IsError = true,
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { code = e.Code, message = e.Message }) }] };
        }
        catch (DbUpdateConcurrencyException)
        {
            return new CallToolResult { IsError = true,
                Content = [new TextContentBlock { Text = "{\"code\":\"version_conflict\",\"message\":\"Read the resource again\"}" }] };
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "uq_steps_project_position" })
        {
            return new CallToolResult { IsError = true,
                Content = [new TextContentBlock { Text = "{\"code\":\"version_conflict\",\"message\":\"The step list changed\"}" }] };
        }
    });

var app = builder.Build();
if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Corvees")))
    throw new InvalidOperationException("ConnectionStrings:Corvees must be set.");
if (string.IsNullOrWhiteSpace(app.Configuration["AllowedHosts"]) || app.Configuration["AllowedHosts"] == "*")
    throw new InvalidOperationException("AllowedHosts must list the expected host names.");

app.UseExceptionHandler(error => error.Run(async context =>
{
    context.Response.StatusCode = 500;
    await context.Response.WriteAsJsonAsync(new { code = "internal_error", message = "Unexpected server error" });
}));

app.MapGet("/healthz", () => Results.Ok());
app.MapGet("/readyz", async (CorveesDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok() : Results.StatusCode(503));

app.UseRouting();
app.Use(async (context, next) =>
{
    var mcp = context.Request.Path.StartsWithSegments("/m");
    var rest = context.Request.Path.StartsWithSegments("/api/v1") && context.Request.Path != "/api/v1";
    if (!mcp && !rest) { await next(context); return; }

    var token = mcp ? context.Request.RouteValues["token"] as string
        : context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? context.Request.Headers.Authorization.ToString()[7..] : null;
    if (token is null || token.Length != 64 || !token.All(Uri.IsHexDigit))
    {
        context.Response.StatusCode = mcp ? 404 : 401;
        return;
    }
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    var db = context.RequestServices.GetRequiredService<CorveesDbContext>();
    var member = await db.Members.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TokenHash == hash && x.DeletedAt == null);
    if (member == null)
    {
        context.Response.StatusCode = mcp ? 404 : 401;
        return;
    }
    db.GroupId = member.GroupId;
    context.Items["member"] = new MemberSession(member);
    await next(context);
});

app.MapApi();
app.MapMcp("/m/{token}/mcp");
app.Run();

public partial class Program;
