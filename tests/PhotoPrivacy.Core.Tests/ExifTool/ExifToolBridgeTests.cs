using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolBridgeTests
{
    // 返修：AppConfig.Default.ExifTool.Path 在 Unix 回退裸 "exiftool"（相对路径），
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
        Assert.DoesNotContain("-ver", healthCommand, StringComparison.OrdinalIgnoreCase);

        // 合同变更：本断言原先锁定的是缺陷本身——健康探针把 exiftool 自身路径
        // 当「待解析文件」喂入（原为 Assert.Contains(Config.ExifTool.Path, healthCommand, ...)）。
        // 现翻转为回归守卫：轻量探针不得携带任何文件参数。
        Assert.DoesNotContain(Config.ExifTool.Path, healthCommand, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Not_Restart_Per_File_When_Health_Probe_Is_File_Free()
    {
        // 行为测试（抓住原 bug）：原健康探针把 exiftool 自身路径当「待解析文件」喂入，
        // ExifTool 真去解析那个二进制 → 慢机上 >500ms 触发 health_timeout → 每处理一个文件就整进程重启，
        // 摧毁 stay_open 常驻收益。本 fake 把「命令块含文件参数」折算成 900ms 回包延迟
        // （> RunningHealthTimeout 500ms）：旧探针必然超时重启（StartCalls > 1），
        // 新探针不含文件参数 → 恒健康 → 零重启。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            FileInputParseDelayMs = 900
        };
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(0, process.StopCalls);
    }

    [Fact]
    public async Task EnsureStartedAsync_Concurrent_Health_Failures_Should_Serialize_Restart()
    {
        // 行为测试（抓住原 bug）：重启路径原不持 _startLock，
        // 多个调用方同时探到 unhealthy 时会并发 Stop/Start 同一进程（重启风暴 + _started 交错）。
        // 故障注入为进程级静默 → 三次并发调用都会探到 unhealthy；
        // 修复后重启被 _startLock 串行化 → StartAsync 并发进入峰值恒为 1。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            SilenceOutput = true,
            StartDelayMs = 100
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(100));

        await bridge.EnsureStartedAsync(CancellationToken.None);

        await Task.WhenAll(
            Enumerable.Range(0, 3).Select(_ => Task.Run(() => bridge.EnsureStartedAsync(CancellationToken.None))));

        Assert.Equal(1, process.MaxConcurrentStartCalls);
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
    public async Task EnsureVersionAsync_Should_Probe_Version_After_First_Probe_Timed_Out()
    {
        // #86 回归：版本探针只在冷启动跑一次，超时后该桥实例会一直上报 unknown。
        // 状态消费者驱动的版本解析必须能补探到真实版本——ExifTool 本身一直是可用的。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionResponseDelayMs = 400
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(80));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        Assert.Equal(ExifToolVersionText.Unknown, bridge.VersionText);

        // 探针不再超时（例如首次冷启动被杀软拖慢，后续恢复正常）。
        process.VersionResponseDelayMs = 0;
        await bridge.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal("13.20", bridge.VersionText);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Not_Probe_Version_For_File_Processing_Path()
    {
        // 防吞吐回归：文件处理热路径（每文件 EnsureStartedAsync）不得再跑版本探针——
        // 探针超时预算 3 s，挂在热路径上会让降级态每文件多付一次超时。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionResponseDelayMs = 400
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(80));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Equal(ExifToolVersionText.Unknown, bridge.VersionText);
        Assert.Single(process.Writes.Where(w => w.Contains("-ver", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Not_ReProbe_When_Version_Already_Known()
    {
        // 补探只服务「还没有版本」这一状态；版本就绪后不得再产生 -ver 往返。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureVersionAsync(CancellationToken.None);
        await bridge.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal("13.20", bridge.VersionText);
        Assert.Single(process.Writes.Where(w => w.Contains("-ver", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Recover_Version_When_Process_Died_After_Timed_Out_Probe()
    {
        // 进程崩溃/被杀后 _started 仍为 true：只补探会往死管道写、异常被吞、版本永久 unknown。
        // 版本解析入口必须先过健康检查（必要时重启），再补探。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionResponseDelayMs = 400
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(80));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        Assert.Equal(ExifToolVersionText.Unknown, bridge.VersionText);

        // 桥不知道进程已经没了（模拟外部崩溃）。
        await process.StopAsync(CancellationToken.None);
        process.VersionResponseDelayMs = 0;

        await bridge.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal("13.20", bridge.VersionText);
        Assert.True(process.StartCalls >= 2, "崩溃后必须重新拉起进程才能解析版本");
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Not_Restart_A_Live_But_Slow_Process()
    {
        // CodeRabbit 保留意见成立的形态（#86）：状态轮询不得把「只是错过 500 ms 健康应答」的
        // 活进程判成不健康并重启——重启会打断在途擦除，让它的输出被新进程的 {ready} 顶成
        // 「无元数据」而静默跳过该文件。进程还在就只补探。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            VersionResponseDelayMs = 400
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(80));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        Assert.Equal(ExifToolVersionText.Unknown, bridge.VersionText);

        // 进程仍活着，但健康应答比 500 ms 预算慢；版本探针本身正常。
        process.HealthResponseDelayMs = 900;
        process.VersionResponseDelayMs = 0;

        await bridge.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal("13.20", bridge.VersionText);
        Assert.Equal(1, process.StartCalls);
        Assert.Equal(0, process.StopCalls);
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Not_Start_Process_On_Retired_Bridge()
    {
        // CodeRabbit 第二轮 Major（#86）：ReloadConfig 会先 Stop 再 Dispose 旧桥。若热重载窗口内的
        // 状态探针还能在旧桥上拉起进程，那个子进程无人回收 → 泄漏。退休桥必须拒绝启动。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.StopAsync(CancellationToken.None);
        await bridge.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal(0, process.StartCalls);
        Assert.Equal(ExifToolVersionText.Unknown, bridge.VersionText);
    }

    [Fact]
    public async Task EnsureVersionAsync_Concurrent_With_File_Path_Should_Start_Once_And_Never_Restart()
    {
        // CodeRabbit 第三轮（#86）：版本路径观察到「进程已停」后等锁，其间文件路径可能已经启动
        // 进程并开始处理文件。此时若走健康检查，忙进程会超时 → 重启 → 打断在途擦除。
        // 这里并发驱动两条路径：只允许启动一次、零重启。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            StartDelayMs = 60
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(80));

        var filePath = Task.Run(() => bridge.EnsureStartedAsync(CancellationToken.None));
        var versionPath = Task.Run(() => bridge.EnsureVersionAsync(CancellationToken.None));

        await Task.WhenAll(filePath, versionPath).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(0, process.StopCalls);
        Assert.Equal("13.20", bridge.VersionText);
    }

    [Fact]
    public async Task StopAsync_Should_Stop_A_Process_Started_By_An_Admitted_Start()
    {
        // ARCH-EXIFTOOL-RETIREMENT-001（CodeRabbit 第四轮）：启动已进锁、StopAsync 后到时，
        // 必须由 StopAsync 在锁内把刚起的进程停掉，而不是在退役桥上留下一个无人回收的子进程。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            StartDelayMs = 150
        };
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(80));

        var start = bridge.EnsureStartedAsync(CancellationToken.None);   // 同步段已拿锁并进入启动
        await Task.Delay(30);                                            // 让启动在锁内阻塞于慢启动

        var stop = bridge.StopAsync(CancellationToken.None);

        await Task.WhenAll(start, stop).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(1, process.StopCalls);
        Assert.False(process.IsRunning);
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Not_Start_When_Stop_Already_Won()
    {
        // 退役先到：版本路径即便拿到旧桥引用也不得启动进程。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        var stop = bridge.StopAsync(CancellationToken.None);   // 同步段即置终态
        var version = bridge.EnsureVersionAsync(CancellationToken.None);

        await Task.WhenAll(stop, version).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, process.StartCalls);
        Assert.Equal(ExifToolVersionText.Unknown, bridge.VersionText);
    }

    [Fact]
    public async Task HealthCheck_Should_Time_Out_When_Stdin_Gate_Is_Held_By_A_Stalled_Write()
    {
        // 审计残留（与 ARCH-EXIFTOOL-RETIREMENT 同源的准入缺口）：stdin 闸被一次卡死的擦除写入
        // 长期占住时，健康检查必须在自己 500 ms 预算内超时并走恢复，而不是无限排队等闸。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config, healthTimeout: TimeSpan.FromMilliseconds(150));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        Assert.Equal("13.20", bridge.VersionText);

        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.BlockWrites = release;

        using var stallCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var stalled = Task.Run(() => bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "stalled.jpg"),
            stallCts.Token));

        // 等到卡死的那次写入真的落在闸上（冷启动已有 1 次写入），避免用固定等待赌时序。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (process.Writes.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(process.Writes.Count >= 2, "stalled 写入未取得 stdin 闸");

        // 预算被闸吃掉时必须超时 → 重启（StartCalls 增加），而不是挂死。
        await bridge.EnsureStartedAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(process.StartCalls >= 2, "闸被占死时健康检查必须超时并走重启恢复");

        release.TrySetResult(true);
        await Record.ExceptionAsync(() => stalled).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task EnsureVersionAsync_Should_Start_Process_When_Not_Started()
    {
        // 状态查询驱动的版本解析：桥还没启动时不得只返回 unknown，必须冷启动并拿到真实版本。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureVersionAsync(CancellationToken.None);

        Assert.Equal(1, process.StartCalls);
        Assert.Equal("13.20", bridge.VersionText);
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
        // 未知格式立即跳过——不进 ExifTool、不写协议块、不注册等待 marker。
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
        // 已知族但规则产出空参数（no_rules）时同样不得注册等待 marker。
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



