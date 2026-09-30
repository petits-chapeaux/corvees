namespace Corvees.Host.Hosting;

public static class HostConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Corvees")))
            throw new InvalidOperationException("ConnectionStrings:Corvees must be set.");
        if (string.IsNullOrWhiteSpace(configuration["AllowedHosts"]) || configuration["AllowedHosts"] == "*")
            throw new InvalidOperationException("AllowedHosts must list the expected host names.");
    }
}
