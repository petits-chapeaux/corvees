using System.Security.Cryptography;
using System.Text;
using Corvees.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public sealed class IntegrationHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    private HttpClient? _client;
    private readonly List<Guid> _groupIds = [];
    private readonly string? _connection = Environment.GetEnvironmentVariable("CORVEES_TEST_DATABASE_URL");

    public HttpClient Client => _client ?? throw new InvalidOperationException("PostgreSQL integration tests are not configured.");
    public string Token { get; } = NewToken();
    public string OtherToken { get; } = NewToken();
    public QueryRecorder Queries { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Corvees"] = _connection ?? "Host=127.0.0.1;Port=1;Database=corvees;Username=corvees;Password=local-only;Timeout=1"
        }));
        builder.ConfigureServices(services => services.AddDbContext<CorveesDbContext>(options => options.AddInterceptors(Queries)));
    }

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(_connection))
            return;
        _client = CreateClient();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CorveesDbContext>();
        await db.Database.MigrateAsync();
        foreach (var token in new[] { Token, OtherToken })
        {
            var group = new Group { Name = "Test " + Guid.NewGuid() };
            _groupIds.Add(group.Id);
            db.Groups.Add(group);
            db.Members.Add(new Member
            {
                GroupId = group.Id,
                DisplayName = "Test member",
                TokenHash = Hash(token)
            });
        }
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        if (_client != null)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CorveesDbContext>();
            await db.StepDependencies.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.GroupId)).ExecuteDeleteAsync();
            await db.ProjectDependencies.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.GroupId)).ExecuteDeleteAsync();
            await db.Steps.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.GroupId)).ExecuteDeleteAsync();
            await db.Projects.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.GroupId)).ExecuteDeleteAsync();
            await db.Locations.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.GroupId)).ExecuteDeleteAsync();
            await db.Members.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.GroupId)).ExecuteDeleteAsync();
            await db.Groups.IgnoreQueryFilters().Where(x => _groupIds.Contains(x.Id)).ExecuteDeleteAsync();
            _client.Dispose();
        }
        await base.DisposeAsync();
    }

    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
