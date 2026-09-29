using System.Security.Cryptography;
using System.Text;
using Corvees.Host;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CorveesDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>().GetConnectionString("Corvees")));
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [] }));

var app = builder.Build();

var capabilityToken = app.Configuration["CapabilityToken"];
if (string.IsNullOrWhiteSpace(capabilityToken) || capabilityToken.Length < 32)
{
    throw new InvalidOperationException("CapabilityToken must be set to a random value of at least 32 characters.");
}

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("Corvees")))
{
    throw new InvalidOperationException("ConnectionStrings:Corvees must be set.");
}

if (string.IsNullOrWhiteSpace(app.Configuration["AllowedHosts"]) || app.Configuration["AllowedHosts"] == "*")
{
    throw new InvalidOperationException("AllowedHosts must list the expected host names.");
}

app.MapGet("/healthz", () => Results.Ok());
app.MapGet("/readyz", async (CorveesDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken) ? Results.Ok() : Results.StatusCode(503));

app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/g") &&
        !TokenMatches(context.Request.RouteValues["token"] as string, capabilityToken))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

app.MapApi();
app.MapMcp("/g/{token}/mcp");
app.Run();

static bool TokenMatches(string? supplied, string expected)
{
    if (supplied is null) return false;
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
}

public partial class Program;
