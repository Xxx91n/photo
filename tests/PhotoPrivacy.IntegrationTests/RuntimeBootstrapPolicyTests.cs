using PhotoPrivacy.Cli;

namespace PhotoPrivacy.IntegrationTests;

public sealed class RuntimeBootstrapPolicyTests
{
    [Fact]
    public void Decide_Should_Run_Service_Control_Shell_When_NoMode_And_Service_Installed()
    {
        var decision = RuntimeBootstrapPolicy.Decide(
            requestedMode: RuntimeMode.Background,
            hasModeOption: false,
            isUserInteractive: true,
            isServiceInstalled: true);

        Assert.Equal(RuntimeMode.Background, decision.EffectiveMode);
        Assert.True(decision.RunUiControlShell);
        Assert.False(decision.UseTrayIcon);
        Assert.Equal("service", decision.RuntimeKind);
    }

    [Fact]
    public void Decide_Should_Run_Tray_Mode_When_NoMode_And_Service_Not_Installed()
    {
        var decision = RuntimeBootstrapPolicy.Decide(
            requestedMode: RuntimeMode.Background,
            hasModeOption: false,
            isUserInteractive: true,
            isServiceInstalled: false);

        Assert.Equal(RuntimeMode.Background, decision.EffectiveMode);
        Assert.False(decision.RunUiControlShell);
        Assert.True(decision.UseTrayIcon);
        Assert.Equal("tray", decision.RuntimeKind);
    }

    [Fact]
    public void Decide_Should_Not_Use_Unified_Mutex_For_Service_Mode()
    {
        var decision = RuntimeBootstrapPolicy.Decide(
            requestedMode: RuntimeMode.Service,
            hasModeOption: true,
            isUserInteractive: false,
            isServiceInstalled: true);

        Assert.Equal(RuntimeMode.Service, decision.EffectiveMode);
        Assert.Equal("service", decision.RuntimeKind);
    }
}
