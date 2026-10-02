namespace Corvees.Host.Hosting;

public static class HostConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Corvees")))
            throw new InvalidOperationException("ConnectionStrings:Corvees must be set.");
        var allowedHosts = configuration["AllowedHosts"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedHosts is null || allowedHosts.Length == 0 || allowedHosts.Any(host => host.Contains('*')))
            throw new InvalidOperationException("AllowedHosts must list the expected host names.");
    }
}
