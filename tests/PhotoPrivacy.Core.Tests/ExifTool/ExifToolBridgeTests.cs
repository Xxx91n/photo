using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolBridgeTests
{
    // 票 28 返修：AppConfig.Default.ExifTool.Path 在 Unix 回退裸 "exiftool"（相对路径），
    // 被 SUT ValidateExifToolPath 拒绝；Bridge 测试只锁 stay_open/生命周期行为，
    // 统一用平台绝对路径假配置（仅校验绝对性，不要求文件存在）。
    private static readonly AppConfig Config = CreateConfig();

    private static AppConfig CreateConfig()
    {
        var absolutePath = OperatingSystem.IsWindows()
            ? @"C:\Program Files\ExifTool\exiftool.exe"
            : "/usr/bin/exiftool";
        return AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { Path = absolutePath }
        };
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Complete_When_TaskDone_Line_Arrives()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        await bridge.WipeMetadataAsync(Path.Combine(Path.GetTempPath(), "pp-bridge", "a.jpg"), CancellationToken.None);

        Assert.True(process.Writes.Count >= 1);
        Assert.Contains(process.Writes, w => w.Contains("-echo1\nTASK_DONE_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Timeout_And_Remove_Pending_Task_When_Marker_Missing()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            SilenceOutput = true
        };
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bridge.WipeMetadataAsync(Path.Combine(Path.GetTempPath(), "pp-bridge", "timeout.jpg"), cts.Token));

        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Start_Process_When_Not_Started()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(Config.ExifTool.Path, process.ExePath);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Restart_Process_When_HealthCheck_Times_Out()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            SilenceOutput = true
        };
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.True(process.StartCalls >= 2);
        Assert.True(process.StopCalls >= 1);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Not_Restart_When_Health_Response_Arrives_In_300ms()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            HealthResponseDelayMs = 300
        };

        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(0, process.StopCalls);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Probe_ExifTool_Version_On_First_Start()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(process.Writes, w => w.Contains("-ver", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Emit_ExifToolStarted_Lifecycle_Event_On_First_Start()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            Config,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            });

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(lifecycleEvents, ev => ev.EventType == "exiftool_started");
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Include_Start_CommandLine_In_ExifToolStarted_Data()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            Config,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            });

        await bridge.EnsureStartedAsync(CancellationToken.None);

        var started = lifecycleEvents.Last(x => x.EventType == "exiftool_started");
        Assert.NotNull(started.Data);
        Assert.Contains("-stay_open", started.Data!["arguments"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-@", started.Data!["arguments"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Emit_ExifToolRestarted_Lifecycle_Event_When_HealthCheck_Times_Out()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            SilenceOutput = true
        };

        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            Config,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            },
            healthTimeout: TimeSpan.FromMilliseconds(100));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(lifecycleEvents, ev => ev.EventType == "exiftool_restarted");
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Include_Timeout_Reason_In_ExifToolRestarted_Data()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            SilenceOutput = true,
            LastStderrLine = "stderr: timeout"
        };

        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            Config,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            });

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        var restarted = lifecycleEvents.Last(x => x.EventType == "exiftool_restarted");
        Assert.NotNull(restarted.Data);
        Assert.Equal("health_timeout", restarted.Data!["reason"]);
        Assert.Equal("stderr: timeout", restarted.Data!["stderr_last_line"]);
    }

    [Fact]
    public async Task EnsureStartedAsync_HealthCheck_Should_Use_Fast_Path_Probe_With_Marker()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        var healthCommand = process.Writes.Last(x => x.Contains("HEALTH_", StringComparison.Ordinal));
        Assert.Contains("-fast", healthCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-execute", healthCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Config.ExifTool.Path, healthCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("-ver", healthCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Log_Warning_When_WindowsLongPath_Known_Issue_Version_Detected()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionText = "13.05"
        };
        var logger = new ListLogger<ExifToolBridge>();
        var bridge = new ExifToolBridge(process, Config, logger, healthTimeout: TimeSpan.FromMilliseconds(200));

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            x => x.Level == LogLevel.Warning
                && x.Message.Contains("WindowsLongPath", StringComparison.OrdinalIgnoreCase)
                && x.Message.Contains("known stay_open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Allow_Version_Probe_Delay_Above_2Seconds()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionResponseDelayMs = 2200
        };

        var logger = new ListLogger<ExifToolBridge>();
        var bridge = new ExifToolBridge(process, Config, logger: logger);

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.DoesNotContain(
            logger.Entries,
            x => x.Level == LogLevel.Warning
                && x.Message.Contains("version probe timed out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Emit_VersionWarning_Lifecycle_Event_When_Known_Issue_Version_Detected()
    {
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionText = "13.05"
        };

        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            Config,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            },
            healthTimeout: TimeSpan.FromMilliseconds(200));

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(
            lifecycleEvents,
            ev => ev.EventType == "exiftool_version_warning"
                && ev.Message.Contains("WindowsLongPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Return_UnknownFormat_Without_Touching_ExifTool()
    {
        // 票 01（A-001）：未知格式立即跳过——不进 ExifTool、不写协议块、不注册等待 marker。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        var result = await bridge
            .WipeMetadataAsync(Path.Combine(Path.GetTempPath(), "pp-bridge", "legacy.webp"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WipeResult.UnknownFormat, result);
        Assert.Equal(0, process.StartCalls);
        Assert.Empty(process.Writes);
        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Return_Cleaned_NoOp_Without_Waiting_When_No_Rule_Produces_Args()
    {
        // 票 01（A-001）：已知族但规则产出空参数（no_rules）时同样不得注册等待 marker。
        var process = new ProtocolLevelFakeExifToolProcess();
        var rules = new Dictionary<string, bool>
        {
            [FormatRulesStore.Key("jpeg", "strip_all")] = false,
            [FormatRulesStore.Key("jpeg", "preserve_icc")] = true,
            [FormatRulesStore.Key("jpeg", "strip_exif")] = false,
            [FormatRulesStore.Key("jpeg", "strip_xmp")] = false,
            [FormatRulesStore.Key("jpeg", "strip_iptc")] = false,
            [FormatRulesStore.Key("jpeg", "strip_time")] = false
        };

        var bridge = new ExifToolBridge(process, Config, logger: null, lifecycleSink: null, healthTimeout: null, wipeRules: rules);

        var result = await bridge
            .WipeMetadataAsync(Path.Combine(Path.GetTempPath(), "pp-bridge", "a.jpg"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WipeResult.Cleaned_NoOp, result);
        Assert.Equal(0, bridge.PendingCount);
        Assert.DoesNotContain(process.Writes, w => w.Contains("TASK_DONE_", StringComparison.Ordinal));
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}



