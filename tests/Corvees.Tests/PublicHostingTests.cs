using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public sealed class PublicHostingTests
{
    [Theory]
    [InlineData("172.20.0.1", "https", "198.51.100.10")]
    [InlineData("172.20.0.2", "http", "172.20.0.2")]
    public async Task OnlyTrustedProxiesCanForwardHeaders(string remoteAddress, string scheme, string clientAddress)
    {
        using var factory = new PublicHostFactory(remoteAddress);
        using var client = factory.CreateClient();
        using var request = Request("/healthz", "198.51.100.10, 127.0.0.1");
        var response = await client.SendAsync(request);
        Assert.Equal(scheme, response.Headers.GetValues("Test-Scheme").Single());
        Assert.Equal(clientAddress, response.Headers.GetValues("Test-Client").Single());
    }

    [Fact]
    public async Task RateLimitPrecedesAuthenticationAndExcludesHealthChecks()
    {
        using var factory = new PublicHostFactory("172.20.0.1");
        using var client = factory.CreateClient();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("/m/invalid/mcp", "198.51.100.10"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Request("/m/invalid/mcp", "198.51.100.10"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Request("/m/invalid/mcp", "198.51.100.11"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
    }

    [Fact]
    public async Task SpoofingForwardedHeadersCannotBypassRateLimit()
    {
        using var factory = new PublicHostFactory("172.20.0.2");
        using var client = factory.CreateClient();
        for (var i = 0; i < 2; i++)
            await client.SendAsync(Request("/m/invalid/mcp", $"198.51.100.{i}"));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Request("/m/invalid/mcp", "198.51.100.12"))).StatusCode);
    }

    private static HttpRequestMessage Request(string path, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", "https");
        return request;
    }
}

internal sealed class PublicHostFactory(string remoteAddress) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "localhost",
            ["ConnectionStrings:Corvees"] = "Host=127.0.0.1;Port=1;Database=corvees;Username=corvees;Password=local-only;Timeout=1",
            ["ReverseProxy:Address"] = "172.20.0.1",
            ["RateLimiting:PermitLimit"] = "2"
        }));
        builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(new ClientAddressFilter(remoteAddress)));
    }

    private sealed class ClientAddressFilter(string address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => builder =>
        {
            builder.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(address);
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Test-Scheme"] = context.Request.Scheme;
                    context.Response.Headers["Test-Client"] = context.Connection.RemoteIpAddress?.ToString();
                    return Task.CompletedTask;
                });
                return continuation(context);
            });
            next(builder);
        };
    }
}
