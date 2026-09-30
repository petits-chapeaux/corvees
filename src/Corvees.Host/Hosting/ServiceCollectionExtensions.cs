using Corvees.Host.Application;
using Corvees.Host.Http;
using Corvees.Host.Mcp;
using Corvees.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace Corvees.Host.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCorvees(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddDbContext<CorveesDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString("Corvees")));
        services.AddScoped<MemberSession>();
        services.AddScoped<GroupService>();
        services.AddScoped<MemberService>();
        services.AddScoped<LocationService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<ProjectQueries>();
        services.AddScoped<StepService>();
        services.AddScoped<DependencyService>();
        services.AddScoped<BusinessService>();
        services.AddControllers();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler((_, _) => ValueTask.FromResult(new ListToolsResult { Tools = ToolCatalog.Tools.ToList() }))
            .WithCallToolHandler(McpToolHandler.CallAsync);
        return services;
    }
}
