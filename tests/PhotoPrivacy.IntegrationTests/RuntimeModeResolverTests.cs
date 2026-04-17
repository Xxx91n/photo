using Microsoft.Extensions.Configuration;
using PhotoPrivacy.Cli;

namespace PhotoPrivacy.IntegrationTests;

public sealed class RuntimeModeResolverTests
{
    [Fact]
    public void Resolve_Should_Default_To_Background_When_Mode_Missing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var mode = RuntimeModeResolver.Resolve(configuration);

        Assert.Equal(RuntimeMode.Background, mode);
    }

    [Fact]
    public void Resolve_Should_Return_Service_Mode()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["mode"] = "service"
        }).Build();

        var mode = RuntimeModeResolver.Resolve(configuration);

        Assert.Equal(RuntimeMode.Service, mode);
    }

    [Fact]
    public void Resolve_Should_Return_Cli_Mode()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["mode"] = "cli"
        }).Build();

        var mode = RuntimeModeResolver.Resolve(configuration);

        Assert.Equal(RuntimeMode.Cli, mode);
    }
}
