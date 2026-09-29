using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class LocalHostTests : IClassFixture<LocalHostFactory>
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private readonly HttpClient _client;

    public LocalHostTests(LocalHostFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task HealthDoesNotDependOnDatabase()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessFailsWhenDatabaseIsUnavailable()
    {
        var response = await _client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task McpRequiresCapabilityToken()
    {
        var response = await _client.PostAsJsonAsync("/g/invalid/mcp", new { jsonrpc = "2.0", method = "initialize", id = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task McpListsNoBusinessTools()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/g/{Token}/mcp")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 2, method = "tools/list" })
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"tools\":[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task McpInitializesWithoutBusinessTools()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/g/{Token}/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "test", version = "1.0" } }
            })
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("serverInfo", await response.Content.ReadAsStringAsync());
    }
}

public sealed class LocalHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CapabilityToken"] = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            ["ConnectionStrings:Corvees"] = "Host=127.0.0.1;Port=1;Database=corvees;Username=corvees;Password=local-only;Timeout=1"
        }));
    }
}
