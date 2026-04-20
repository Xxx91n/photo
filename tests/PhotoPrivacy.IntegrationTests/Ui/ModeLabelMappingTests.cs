using PhotoPrivacy.Ui.Views;
using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class ModeLabelMappingTests
{
    [Theory]
    [InlineData("service", "🔵 服务模式")]
    [InlineData("tray", "🟢 托盘模式")]
    [InlineData("other", "other")]
    public void MapModeLabel_Should_Return_Expected_Label(string input, string expected)
    {
        var label = MainWindowMapModeLabelAccessor.Map(input);

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
        var text = MainWindowMapModeLabelAccessor.BuildRuntimeStatusText(runtimeKind, state, isPaused);

        Assert.Equal(expected, text);
    }
}

internal static class MainWindowMapModeLabelAccessor
{
    public static string Map(string runtimeKind)
    {
        var type = typeof(MainWindow);
        var method = type.GetMethod("MapModeLabel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)(method!.Invoke(null, new object[] { runtimeKind })!);
    }

    public static string BuildRuntimeStatusText(string runtimeKind, ServiceRuntimeState state, bool isPaused)
    {
        var type = typeof(MainWindow);
        var method = type.GetMethod("BuildRuntimeStatusText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)(method!.Invoke(null, new object[] { runtimeKind, state, isPaused })!);
    }
}
