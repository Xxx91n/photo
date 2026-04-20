using Microsoft.Extensions.Configuration;
using PhotoPrivacy.Worker;

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

    [Fact]
    public void ResolveFromArgs_Should_Return_Background_Mode()
    {
        var mode = RuntimeModeResolver.ResolveFromArgs(["--mode", "background"]);

        Assert.Equal(RuntimeMode.Background, mode);
    }

    [Fact]
    public void ResolveFromArgs_Should_Return_Service_Mode()
    {
        var mode = RuntimeModeResolver.ResolveFromArgs(["--mode", "service"]);

        Assert.Equal(RuntimeMode.Service, mode);
    }

    [Fact]
    public void ResolveFromArgs_Should_Return_Cli_Mode_With_Inline_Assignment()
    {
        var mode = RuntimeModeResolver.ResolveFromArgs(["--mode=cli"]);

        Assert.Equal(RuntimeMode.Cli, mode);
    }

    [Fact]
    public void ResolveRaw_Should_Default_To_Background_When_Unknown_Mode()
    {
        var mode = RuntimeModeResolver.ResolveRaw("unknown");

        Assert.Equal(RuntimeMode.Background, mode);
    }

    [Fact]
    public void HasModeOption_Should_Return_False_When_Mode_Missing()
    {
        var hasMode = RuntimeModeResolver.HasModeOption(["--once", "true"]);

        Assert.False(hasMode);
    }

    [Fact]
    public void HasModeOption_Should_Return_True_For_Split_Mode_Argument()
    {
        var hasMode = RuntimeModeResolver.HasModeOption(["--mode", "service"]);

        Assert.True(hasMode);
    }

    [Fact]
    public void HasModeOption_Should_Return_True_For_Inline_Mode_Argument()
    {
        var hasMode = RuntimeModeResolver.HasModeOption(["--mode=cli"]);

        Assert.True(hasMode);
    }

    [Fact]
    public void ResolveFromArgs_Should_Return_Background_When_No_Mode_Provided()
    {
        var mode = RuntimeModeResolver.ResolveFromArgs(["--config", "config/config.json"]);

        Assert.Equal(RuntimeMode.Background, mode);
    }

    [Fact]
    public void ResolveFromArgs_Should_Return_Service_When_Mode_Service_Provided()
    {
        var mode = RuntimeModeResolver.ResolveFromArgs(["--mode", "service", "--config", "config/config.json"]);

        Assert.Equal(RuntimeMode.Service, mode);
    }
}
