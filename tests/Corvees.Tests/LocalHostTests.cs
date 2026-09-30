using System.Net;
using System.Net.Http.Json;
using Corvees.Host.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

public sealed class LocalHostTests : IClassFixture<LocalHostFactory>
{
    private readonly HttpClient _client;
    public LocalHostTests(LocalHostFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task HealthDoesNotDependOnDatabase() =>
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/healthz")).StatusCode);

    [Fact]
    public async Task ReadinessFailsWhenDatabaseIsUnavailable() =>
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync("/readyz")).StatusCode);

    [Fact]
    public async Task MetadataRemainsPublic()
    {
        var response = await _client.GetAsync("/api/v1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("v1", info?["version"]);
    }

    [Fact]
    public async Task RestRequiresMemberToken() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/v1/projects")).StatusCode);

    [Fact]
    public async Task McpRejectsMalformedTokenWithoutDatabase()
    {
        var response = await _client.PostAsJsonAsync("/m/invalid/mcp", new { jsonrpc = "2.0", method = "initialize", id = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void EveryPublicToolHasInputAndOutputSchema()
    {
        Assert.Equal(30, ToolCatalog.Tools.Count);
        Assert.All(ToolCatalog.Tools, tool =>
        {
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
            Assert.Equal("object", tool.OutputSchema?.GetProperty("type").GetString());
        });
    }

    [Fact]
    public void ProjectReadsOpenTheProjectBoardView()
    {
        var withView = ToolCatalog.Tools.Where(tool => tool.Meta != null).ToArray();
        Assert.Equal(["list_projects", "get_project"], withView.Select(tool => tool.Name));
        Assert.All(withView, tool => Assert.Equal(AppCatalog.ProjectBoardUri, tool.Meta!["ui"]!["resourceUri"]!.GetValue<string>()));
        Assert.Equal(AppCatalog.ProjectBoardUri, Assert.Single(AppCatalog.Resources).Uri);
    }
}

public sealed class LocalHostFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Corvees"] = "Host=127.0.0.1;Port=1;Database=corvees;Username=corvees;Password=local-only;Timeout=1"
        }));
    }
}
