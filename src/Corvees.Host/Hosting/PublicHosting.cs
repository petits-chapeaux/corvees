using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace Corvees.Host.Hosting;

public static class PublicHosting
{
    public static IServiceCollection AddPublicHosting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 2;
            if (configuration["ReverseProxy:Address"] is { Length: > 0 } address)
            {
                var proxy = IPAddress.Parse(address);
                options.KnownProxies.Add(proxy);
                options.KnownProxies.Add(proxy.MapToIPv6());
            }
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments("/m") && !context.Request.Path.StartsWithSegments("/api/v1"))
                    return RateLimitPartition.GetNoLimiter("operations");
                return RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue("RateLimiting:PermitLimit", 120),
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
        });
        return services;
    }
}
