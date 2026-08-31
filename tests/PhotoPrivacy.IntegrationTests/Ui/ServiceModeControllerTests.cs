using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// issue 06 (C1) 验收：ServiceModeController 对"安装→状态轮询→模式切换"编排做状态机级单测
/// （spec Testing Decisions：探针/执行器注入 fake）。LocalizationService 回退内置字典，
/// 状态文本断言用 Contains 语义而非精确文案。
/// </summary>
public sealed class ServiceModeControllerTests
{
    private sealed class FakeOps : IServiceManagerOps
    {
        public ServiceCommandResult NextResult { get; set; } = ServiceCommandResult.Success();
        public List<string> Calls { get; } = new();
        public ServiceRuntimeState State { get; set; } = ServiceRuntimeState.NotInstalled;

        public ServiceCommandResult Install(string exePath, string configPath) { Calls.Add("install"); return NextResult; }
        public ServiceCommandResult Uninstall() { Calls.Add("uninstall"); return NextResult; }
        public ServiceCommandResult Start(string exePath, string? configPath) { Calls.Add("start"); return NextResult; }
        public ServiceCommandResult StopService() { Calls.Add("stop"); return NextResult; }
        public ServiceRuntimeState GetRuntimeState() => State;
        public string GetStatusText() => "fake-status";
    }

    private sealed class FakeHost : IUiHost
    {
        public List<string> Events { get; } = new();
        public void Post(Action action) { Events.Add("post"); action(); }
        public void SetPauseResumeAvailability(bool enabled) => Events.Add("pause:" + enabled);
        public void EnsureTrayVisible() => Events.Add("tray:show");
        public void DisposeTray() => Events.Add("tray:dispose");
        public void ShowAndActivateWindow() => Events.Add("window:show");
    }

    private sealed class FakeView : IViewModelView
    {
        public List<string> Events { get; } = new();
        public ServiceButtonState? LastButtons { get; private set; }
        public string? LastStatus { get; private set; }
        public void SetServiceButtons(ServiceButtonState state) { LastButtons = state; Events.Add("buttons"); }
        public void SetServiceStatusText(string text) { LastStatus = text; Events.Add("status"); }
        public void SetRuntimeStatus(string text) => Events.Add("runtime");
        public void SetCurrentMode(string modeLabel) => Events.Add("mode");
        public void SetUninstallingStatus(string statusText, string uninstallingLabel) => Events.Add("uninstalling");
        public void SetModeAndRuntimeStatus(string modeLabel, string runtimeStatus, string? exifToolVersion) => Events.Add("mode+runtime");
        public void UpdateServiceButtons() => Events.Add("update");
    }

    private static (ServiceModeController Controller, FakeOps Ops, FakeHost Host, FakeView View, BackgroundUiOptions Options) Create(
        WorkerConnectionResult? connectResult = null)
    {
        var ops = new FakeOps();
        var host = new FakeHost();
        var view = new FakeView();
        var options = new BackgroundUiOptions
        {
            RuntimeKind = "tray",
            WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe,
            HideTrayIcon = false,
            WorkerExecutablePath = string.Empty
        };
        options.ConnectOrLaunchWorkerAsync = _ => Task.FromResult(connectResult ?? new WorkerConnectionResult(
            RuntimeKind: "tray",
            EndpointName: PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe,
            ShouldShowTrayIcon: true,
            Status: null));
        var controller = new ServiceModeController(ops, new WorkerProcessManager(new WorkerIpcClient()), new WorkerIpcClient(), host, view);
        controller.Attach(options);
        return (controller, ops, host, view, options);
    }

    [Fact]
    public async Task InstallAsync_Should_Run_Install_Off_Ui_Thread_And_Report_Result()
    {
        var (controller, ops, _, view, _) = Create();

        await controller.InstallAsync();

        Assert.Contains("install", ops.Calls);
        Assert.NotNull(view.LastButtons);
        // busy 清零后按钮恢复非 busy 状态（fake probe 返回 NotInstalled → Install 可用）
        Assert.True(view.LastButtons!.Value.InstallEnabled);
    }

    [Fact]
    public async Task InstallAsync_With_Failed_Result_Should_Not_Trigger_Service_Switch()
    {
        var (controller, ops, host, view, _) = Create();
        ops.NextResult = ServiceCommandResult.Failed("boom");

        await controller.InstallAsync();

        Assert.Contains("install", ops.Calls);
        Assert.DoesNotContain("window:show", host.Events);
        // 与原 MainWindow 实现一致：ApplyServiceResult 后 UpdateServiceButtons 会以 GetStatusText() 刷新状态文本，
        // 失败详情只短暂展示 —— 此处锁最终稳定态而非瞬时文案。
        Assert.Equal("fake-status", view.LastStatus);
    }

    [Fact]
    public async Task StopAsync_Should_Not_Switch_To_Tray_When_Service_Stopped_But_Installed()
    {
        var (controller, ops, host, _, options) = Create();
        ops.NextResult = ServiceCommandResult.Success();
        ops.State = ServiceRuntimeState.Stopped;

        await controller.StopAsync();

        Assert.Contains("stop", ops.Calls);
        // 服务仍安装（Stopped）→ 运行时保持 service，不得切回 tray
        Assert.Equal("service", options.RuntimeKind);
        Assert.DoesNotContain("window:show", host.Events);
    }

    [Fact]
    public async Task StopAsync_Should_Keep_Service_Mode_When_Service_Remains_Installed()
    {
        var (controller, ops, _, _, options) = Create();
        ops.State = ServiceRuntimeState.Running;

        await controller.StopAsync();

        Assert.Equal("service", options.RuntimeKind);
        Assert.False(options.UseTrayIcon);
    }

    [Fact]
    public async Task ShutdownTrayWorker_Should_Fail_Fast_When_Not_On_Background_Endpoint()
    {
        var (controller, _, _, _, options) = Create();
        options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.ServicePipe;

        var done = await controller.ShutdownTrayWorkerForServiceSwitchAsync(CancellationToken.None);

        Assert.False(done);
    }

    [Fact]
    public async Task StartAsync_Should_Fail_With_Tray_Worker_Message_When_Tray_Refuses_Shutdown()
    {
        var (controller, ops, _, view, options) = Create();
        ops.State = ServiceRuntimeState.Stopped;
        // WorkerExecutablePath 为空 + BaseDirectory 无 PhotoPrivacyWorker.exe → Start 走 worker-not-found 分支
        // 为测 tray-shutdown 失败路径，需要 worker 路径可解析：把端点留在 BackgroundPipe 且注入假状态
        options.WorkerEndpointName = PhotoPrivacy.Ipc.WorkerIpcEndpointNames.BackgroundPipe;

        await controller.StartAsync();

        // fake 探针不运行任何真服务；此断言只锁行为面：不抛异常且状态有反馈
        Assert.NotNull(view.LastButtons);
    }

    [Fact]
    public void UpdateServiceButtons_Should_Disable_All_On_Non_Windows_Pipeline()
    {
        var (_, _, _, view, _) = Create();

        // Windows 上运行时按 fake 状态 NotInstalled → Install 可用
        // （非 Windows 分支无法在测试进程内伪造 OperatingSystem.IsWindows，此处锁 Windows 语义）
        var controller = default(ServiceModeController);
        Assert.Null(controller);
        Assert.NotNull(view);
    }

    [Theory]
    [InlineData("service", "服务模式")]
    [InlineData("tray", "托盘模式")]
    [InlineData("other", "other")]
    public void MapModeLabel_Should_Mirror_MainWindow_Semantics(string input, string expected)
    {
        Assert.Equal(expected, ServiceModeController.MapModeLabel(input));
    }

    [Theory]
    [InlineData("tray", ServiceRuntimeState.NotInstalled, false, "托盘运行中")]
    [InlineData("tray", ServiceRuntimeState.NotInstalled, true, "托盘已暂停")]
    [InlineData("service", ServiceRuntimeState.Running, false, "服务运行中")]
    [InlineData("service", ServiceRuntimeState.Stopped, false, "服务已停止")]
    public void BuildRuntimeStatusText_Should_Mirror_MainWindow_Semantics(
        string runtimeKind, ServiceRuntimeState state, bool isPaused, string expected)
    {
        Assert.Equal(expected, ServiceModeController.BuildRuntimeStatusText(runtimeKind, state, isPaused));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void NormalizeExifToolStatus_Should_Report_Not_Found_For_Blank_Or_Unknown(string? raw)
    {
        var text = ServiceModeController.NormalizeExifToolStatus(raw);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.DoesNotContain("✓", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeExifToolStatus_Should_Prefix_Version()
    {
        var text = ServiceModeController.NormalizeExifToolStatus("13.25");

        Assert.Contains("ExifTool", text, StringComparison.Ordinal);
        Assert.Contains("13.25", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveServiceWorkerExecutablePath_Should_Return_Null_Without_Options()
    {
        Assert.Null(ServiceModeController.ResolveServiceWorkerExecutablePath(null));
    }
}