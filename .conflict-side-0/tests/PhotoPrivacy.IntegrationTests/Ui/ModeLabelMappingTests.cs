using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06: MapModeLabel/BuildRuntimeStatusText 已从 MainWindow 抽取为
/// ServiceModeController 公共静态方法，本测试直调新家断言语义不变。
/// </summary>
public sealed class ModeLabelMappingTests
{
    [Theory]
    [InlineData("service", "服务模式")]
    [InlineData("tray", "托盘模式")]
    [InlineData("other", "other")]
    public void MapModeLabel_Should_Return_Expected_Label(string input, string expected)
    {
        var label = ServiceModeController.MapModeLabel(input);

        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("tray", ServiceRuntimeState.NotInstalled, false, "托盘运行中")]
    [InlineData("tray", ServiceRuntimeState.NotInstalled, true, "托盘已暂停")]
    [InlineData("service", ServiceRuntimeState.Running, false, "服务运行中")]
    [InlineData("service", ServiceRuntimeState.Stopped, false, "服务已停止")]
    public void BuildRuntimeStatusText_Should_Return_Mature_User_Wording(
        string runtimeKind,
        ServiceRuntimeState state,
        bool isPaused,
        string expected)
    {
        var text = ServiceModeController.BuildRuntimeStatusText(runtimeKind, state, isPaused);

        Assert.Equal(expected, text);
    }
}