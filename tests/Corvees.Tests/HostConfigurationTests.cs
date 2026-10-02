using Corvees.Host.Hosting;
using Microsoft.Extensions.Configuration;

public sealed class HostConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ; ")]
    [InlineData("*")]
    [InlineData("*;localhost;127.0.0.1")]
    [InlineData("*.example.com;localhost")]
    public void HostsMustBeExplicit(string? hosts)
    {
        var configuration = Configuration(hosts);
        Assert.Throws<InvalidOperationException>(() => HostConfiguration.Validate(configuration));
    }

    [Fact]
    public void PublicHostAndLoopbackAreAccepted() =>
        HostConfiguration.Validate(Configuration("corvees.example.com;localhost;127.0.0.1"));

    private static IConfiguration Configuration(string? hosts) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Corvees"] = "Host=postgres;Database=corvees",
            ["AllowedHosts"] = hosts
        }).Build();
}
