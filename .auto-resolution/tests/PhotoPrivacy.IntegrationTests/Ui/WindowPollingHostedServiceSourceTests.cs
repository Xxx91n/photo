namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 31（架构恢复第七轮）轮询宿主服务 source-lint：
/// - 版本轮询 + 服务状态轮询脱离 MainWindow code-behind，下沉 WindowPollingHostedService（IHostedService）
/// - 循环体/周期/取消/Dispatcher 投递语义逐字保持（1s 版本轮询、3s 服务模式轮询）
/// - 检查点 B：MainWindow code-behind 行数持续下降（基线 1158 = 票 30 收口 blob，阈值放宽 5%）
/// - 装配破环：宿主服务只依赖 VM 单例，服务模式轮询经 AttachServiceModePoll 委托由窗口构造挂载
/// </summary>
public sealed class WindowPollingHostedServiceSourceTests
{
    private static string UiPath(params string[] segs) => Path.Combine(new[] { SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui" }.Concat(segs).ToArray());

    private static string Read(string path) => File.ReadAllText(path, System.Text.Encoding.UTF8);

    [Fact]
    public void MainWindow_Should_Not_Own_Polling_Loops()
    {
        // 轮询循环/字段/拉起形态不得回潮（考古 // 注释豁免，与票 30-fix 教训一致：剥注释后断言）
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        foreach (var banned in new[]
                 {
                     "_versionSnapshot",
                     "_versionPollCts",
                     "_versionPollTask",
                     "_serviceModePollCts",
                     "_serviceModePollTask",
                     "PollVersionAsync",
                     "new ExifToolVersionSnapshot(",
                     "PollServiceModeTransitionAsync("
                 })
        {
            Assert.DoesNotContain(banned, stripped, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void MainWindow_Should_Wire_HostedService_Only()
    {
        // 窗口只剩三行接线：构造注入 + 委托挂载 + Activate/一次性版本读取/StopAsync 调用
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        Assert.Contains("WindowPollingHostedService pollingHostedService", stripped, StringComparison.Ordinal);
        Assert.Contains("_pollingHostedService.AttachServiceModePoll(_serviceModeController.PollServiceModeTransitionAsync)", stripped, StringComparison.Ordinal);
        Assert.Contains("_pollingHostedService.Activate()", stripped, StringComparison.Ordinal);
        Assert.Contains("_pollingHostedService.ApplyExifToolVersionFromIpcAsync", stripped, StringComparison.Ordinal);
        Assert.Contains("await _pollingHostedService.StopAsync(CancellationToken.None)", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void HostedService_Should_Keep_Polling_Semantics()
    {
        // 循环体迁入后语义锁：1s/3s 周期、变化检测、Dispatcher 投递、IHostedService 契约
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Services", "WindowPollingHostedService.cs");
        Assert.Contains("IHostedService", stripped, StringComparison.Ordinal);
        Assert.Contains("Task.Delay(TimeSpan.FromSeconds(1), token)", stripped, StringComparison.Ordinal);
        Assert.Contains("TryReadChangedAsync", stripped, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.UIThread.Post", stripped, StringComparison.Ordinal);
        Assert.Contains("Activate()", stripped, StringComparison.Ordinal);
        Assert.Contains("AttachServiceModePoll", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void HostedService_Should_Keep_Cancel_And_Dispose_Semantics()
    {
        // 释放语义锁：CancelAsync → Dispose → null 顺序 + await 容忍 OperationCanceledException
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Services", "WindowPollingHostedService.cs");
        Assert.Contains("StopAsync", stripped, StringComparison.Ordinal);
        foreach (var required in new[]
                 {
                     "CancelAsync()",
                     ".Dispose()",
                     "OperationCanceledException"
                 })
        {
            Assert.Contains(required, stripped, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HostedService_Should_Not_Depend_On_ServiceModeController()
    {
        // 装配破环锁：直接依赖 ServiceModeController 会成 MS DI 死环
        //（Controller 构造需 MainWindow 视图适配器）；委托挂载是唯一通道。
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Services", "WindowPollingHostedService.cs");
        foreach (var banned in new[]
                 {
                     "ServiceModeController serviceModeController",
                     "_serviceModeController.",
                     "readonly ServiceModeController"
                 })
        {
            Assert.DoesNotContain(banned, stripped, StringComparison.Ordinal);
        }
        // NormalizeExifToolStatus 是 ServiceModeController 的静态方法，合法直引
        Assert.Contains("ServiceModeController.NormalizeExifToolStatus", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_CodeBehind_Should_Shrink_Vs_Ticket30_Baseline()
    {
        // 检查点 B：基线 1158 行（票 30 收口 blob 实测），阈值放宽 5% 防 CI 平台行尾差异；
        // 实际降幅见 report-31。行数口径须用原始全文（含注释空行，与票 30 同口径）。
        var source = Read(UiPath("Views", "MainWindow.axaml.cs"));
        var lines = source.Replace("\r\n", "\n").Split('\n').Length;
        Assert.True(lines <= 1100, $"MainWindow.axaml.cs must shrink vs ticket-30 baseline 1158 (≤1100), got {lines}");
    }
}
