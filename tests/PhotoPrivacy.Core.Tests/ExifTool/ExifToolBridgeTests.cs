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
    public async Task WipeMetadataAsync_Should_Complete_When_Block_Ready_Line_Arrives()
    {
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        await bridge.WipeMetadataAsync(Path.Combine(Path.GetTempPath(), "pp-bridge", "a.jpg"), CancellationToken.None);

        Assert.True(process.Writes.Count >= 1);
        Assert.Contains(process.Writes, w => w.Contains("-echo1\nTASK_DONE_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Not_Return_Cleaned_Before_Block_Ready_Signal()
    {
        // 防的具体回潮形态（issue #97）：桥一匹配 TASK_DONE_ 回显就完成等待——回显是 ExifTool 在
        // 参数解析阶段打的，磁盘写入要到本块 {ready} 才保证完成，于是擦除会在还没写盘时就返回
        // Cleaned_Modified（进程随后被杀即静默漏清理）。这里让回显照常先出、把块结束信号推迟：
        // 修复后返回必须发生在该块的 {ready} 之后。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            LateReadySignalMs = 800
        };
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        var wipe = Task.Run(() => bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "late-ready.jpg"),
            CancellationToken.None));

        // fake 先派发再入列，因此看到这一行时桥的 stdout 处理器已经把 TASK_DONE_ 回显消费掉了。
        await WaitUntilStdoutLineAsync(process, "TASK_DONE_");

        // 结清窗口 150 ms 远小于 {ready} 的 800 ms 迟到量：它不赌回显何时到（回显由上一行等），
        // 只给「一回显就结清」那条旧路径的续跑留出落地时间，旧实现必红。
        await Task.Delay(150);

        Assert.False(wipe.IsCompleted);

        Assert.Equal(WipeResult.Cleaned_Modified, await wipe);
        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Remove_Pending_Task_And_Not_Report_Cleaned_When_Process_Dies_Before_Ready_Signal()
    {
        // 防的具体回潮形态（issue #97 AC2）：进程在 {ready} 前被杀时，桥要么把 TASK_DONE_ 回显当成
        // 完成、直接返回 Cleaned_Modified（文件其实还没写完 → 静默漏清理），要么挂在等待里直到调用方
        // 取消；更糟的是新进程的第一个 {ready} 会被旧进程留下的等待方读成「自己那块结束了」。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            LateReadySignalMs = 5000
        };
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        var wipe = Task.Run(() => bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "killed-before-ready.jpg"),
            CancellationToken.None));

        await WaitUntilStdoutLineAsync(process, "TASK_DONE_");

        // 这一代进程再也不会发出 {ready}。
        await process.StopAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => wipe.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("TASK_DONE_", ex.Message, StringComparison.Ordinal);
        Assert.Contains("{ready}", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Be_Canceled_And_Not_Report_Cleaned_When_Bridge_Stops_Before_Ready_Signal()
    {
        // 防的具体回潮形态（issue #97 影响段的另一条杀手路径）：停机/配置热重载走桥的 StopAsync，
        // 退役时必须把在途擦除判成取消，而不是让它带着「回显已到」的乐观结论返回 Cleaned_Modified。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            LateReadySignalMs = 5000
        };
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        var wipe = Task.Run(() => bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "stopped-before-ready.jpg"),
            CancellationToken.None));

        await WaitUntilStdoutLineAsync(process, "TASK_DONE_");

        await bridge.StopAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wipe);
        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Fail_Not_Report_Cleaned_When_HealthCheck_Restarts_Process_During_Wipe()
    {
        // 防的具体回潮形态（issue #97 影响段第一条杀手路径）：文件处理路径自己的健康检查超时→重启，
        // 在途擦除的 marker 就此失去归属。原实现要么已经按回显返回了 Cleaned_Modified，要么挂到
        // 调用方取消；重启后新进程的第一个 {ready} 还可能被旧等待方读成自己那块结束了。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            LateReadySignalMs = 5000
        };
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        var wipe = Task.Run(() => bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "restarted-mid-wipe.jpg"),
            CancellationToken.None));

        await WaitUntilStdoutLineAsync(process, "TASK_DONE_");

        // 活进程应答比 500 ms 健康预算慢：经 EnsureStartedAsync（擦除每文件都走的健康检查入口）
        // 探到不健康 → 重启（先 Stop 再 Start）。Stop 那一刻就是本用例要盯的杀死时点。
        process.HealthResponseDelayMs = 900;
        await bridge.EnsureStartedAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => wipe.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Contains("TASK_DONE_", ex.Message, StringComparison.Ordinal);
        Assert.True(process.StartCalls >= 2, "健康检查超时必须走过重启");
        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Keep_One_Wipe_Block_Per_File_When_Waiting_For_Ready_Signal()
    {
        // 防的具体回潮形态（issue #97 AC3）：为等块结束信号再补发一个「问写完没有」的命令块，
        // 或干脆退回每文件 spawn 进程——stay_open 的常驻收益就没了。等 {ready} 只是晚一点读同一块
        // 的输出，不产生额外往返。
        var process = new ProtocolLevelFakeExifToolProcess
        {
            LateReadySignalMs = 50
        };
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            var result = await bridge.WipeMetadataAsync(
                Path.Combine(Path.GetTempPath(), "pp-bridge", $"resident-{i}.jpg"),
                CancellationToken.None);

            Assert.Equal(WipeResult.Cleaned_Modified, result);
        }

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(0, process.StopCalls);
        Assert.Equal(3, process.Writes.Count(w => w.Contains("TASK_DONE_", StringComparison.Ordinal)));
        Assert.Equal(3, process.Writes.Count(w => w.Contains("-json", StringComparison.Ordinal)));
        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Not_Settle_Probe_On_A_Prefixed_Foreign_Probe_Marker()
    {
        // 防的具体回潮形态（issue #111）：探测块用 Contains 认领 PROBE_DONE_{id} 回显，而 id 是桥内自增
        // 整数、marker 之间互为前缀（PROBE_DONE_1 ⊂ PROBE_DONE_10）。于是别的文件（id=10）那块的回显行
        // 会被 id=1 的探测认领，紧跟其后的那句别块的 {ready} 就把 id=1 的探测结清在空输出上 →
        // HasClearableFields 解析不出东西 → Skipped_NoMetadata：文件里的隐私元数据被静默跳过（不擦除、不告警）。
        // 协议级假件把「一块的回显到 {ready} 之间插不进别块的行」当回放前提（#39 的未实测假设），
        // 故本用例改用块产出可延后的假件，把「别块的回显与它的 {ready} 插进本块窗口」这一时序钉死。
        var process = new DeferredBlockFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var dir = Path.Combine(Path.GetTempPath(), "pp-bridge");

        // 10 个并发擦除：第 1 个拿到 PROBE_DONE_1、第 10 个拿到 PROBE_DONE_10（互为前缀）。
        // 逐个发起并等到它把探测块写进来，块序号才与 id 一一对应（id 由桥内自增分配）。
        var wipes = new Task<WipeResult>[10];
        for (var i = 0; i < wipes.Length; i++)
        {
            wipes[i] = bridge.WipeMetadataAsync(Path.Combine(dir, $"prefix-collision-{i}.jpg"), cts.Token);
            await WaitUntilAsync(() => process.Blocks.Count >= i + 1, $"第 {i + 1} 个探测块写入");
        }

        var ownerIndex = process.IndexOfMarker("PROBE_DONE_1");
        var foreignIndex = process.IndexOfMarker("PROBE_DONE_10");
        Assert.True(ownerIndex >= 0, "文件 #1 的探测块未写入");
        Assert.True(foreignIndex >= 0, "文件 #10 的探测块未写入");
        // 前缀关系自证：id 编号方式变了（本用例就不再复现缺陷）时这里先红，而不是默默放行。
        Assert.StartsWith(process.Blocks[ownerIndex].Marker, process.Blocks[foreignIndex].Marker, StringComparison.Ordinal);

        // 别块（id=10）的回显紧跟着它自己的 {ready} 先到，中间没有文件 #1 的探测数据——
        // 这正是把缺陷逼出来的形状：认领判据若是 Contains，文件 #1 会拿这句别块的 {ready} 结清在空输出上。
        process.EmitEcho(foreignIndex);
        process.EmitReady(foreignIndex);

        await Task.Delay(150);
        Assert.False(
            wipes[0].IsCompleted,
            "文件 #1 的探测不得被别块的 PROBE_DONE_10 回显或它的 {ready} 结清（#111）");

        // 自己那块的回显 → 数据 → {ready} 到齐后才结清，且拿到的是自己的探测数据。
        process.EmitEcho(ownerIndex);
        process.EmitBody(ownerIndex);
        process.EmitReady(ownerIndex);

        // 探测结清后擦除块（TASK_DONE_1）才写入，同样只等自己那块的 {ready}。
        await WaitUntilAsync(() => process.IndexOfMarker("TASK_DONE_1") >= 0, "文件 #1 的擦除块写入");
        var wipeIndex = process.IndexOfMarker("TASK_DONE_1");
        process.EmitEcho(wipeIndex);
        process.EmitReady(wipeIndex);

        Assert.Equal(WipeResult.Cleaned_Modified, await wipes[0].WaitAsync(TimeSpan.FromSeconds(10)));

        // 收尾：其余 9 个文件从未被放行（#10 只放行了一个空输出的探测块），取消后必须退干净。
        // 不对它们的结果作断言——本用例只盯文件 #1 的归属，其余块怎么收场与它无关。
        cts.Cancel();
        foreach (var other in wipes.Skip(1))
        {
            _ = await Record.ExceptionAsync(() => other);
        }

        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Report_Skipped_NoMetadata_Only_When_Probe_Output_Is_Really_Empty()
    {
        // 防的具体回潮形态（issue #111 AC3）：归属收紧后探测只由自己那块的 {ready} 结清，
        // 于是「空输出」必须真的来自空输出——不得由别块的回显 / 结清凭空制造出来。
        // 这里让本块的回显与 {ready} 之间没有任何数据行：Skipped_NoMetadata 是唯一合法结论
        // （file_skipped / no_metadata_found 记账语义不变）。
        var process = new DeferredBlockFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        var wipe = bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "really-no-metadata.jpg"),
            CancellationToken.None);

        await WaitUntilAsync(() => process.IndexOfMarker("PROBE_DONE_1") >= 0, "探测块写入");
        var probeIndex = process.IndexOfMarker("PROBE_DONE_1");

        process.EmitEcho(probeIndex);
        process.EmitReady(probeIndex);   // 本块真的没有数据行

        Assert.Equal(WipeResult.Skipped_NoMetadata, await wipe.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, bridge.PendingCount);
        Assert.DoesNotContain(process.Blocks, b => b.Marker.StartsWith("TASK_DONE_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Fail_Probe_Not_Report_NoMetadata_When_Process_Dies_During_Probe()
    {
        // 防的具体回潮形态（issue #111 AC2）：进程在探测途中没了时，本块永远不会再有自己那句 {ready}。
        // 原来的局部 ProbeLineHandler 只会被某一句 {ready} 结清——自己的没来、别人的先来就会误结清空输出
        // （Skipped_NoMetadata，隐私元数据被静默跳过），一个都没来则谁也不收口，调用方挂到取消为止。
        // 修复后探测与擦除共用 #97 的 ProcessExited 收口：认领不到就继续等，进程没了就地判失败。
        var process = new DeferredBlockFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);
        await bridge.StartAsync(CancellationToken.None);

        var wipe = bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "died-during-probe.jpg"),
            CancellationToken.None);

        await WaitUntilAsync(() => process.IndexOfMarker("PROBE_DONE_1") >= 0, "探测块写入");
        var probeIndex = process.IndexOfMarker("PROBE_DONE_1");
        process.EmitEcho(probeIndex);   // 本块已开始处理，但它那句 {ready} 永远不会到

        await process.StopAsync(CancellationToken.None);

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => wipe.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Contains("PROBE_DONE_1", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, bridge.PendingCount);
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
    public async Task Dispose_During_In_Flight_Write_Should_Not_Turn_The_Write_Release_Into_ObjectDisposedException()
    {
        // #126（D12 范围增补）：停机/重载对旧桥的 Dispose 不等待在途写块——WriteBlockAsync 的
        // finally Release 若跨过 Dispose，就命中 ObjectDisposedException（旧实现在 Dispose 里
        // 释放 _stdinGate/_startLock）。形态：探测块写入在途（持 stdin 闸）→ 桥 Stop + Dispose
        //（与 StopRuntimeAsync 的时序一致）→ 放行写入。修复后 Dispose 不再释放信号量，
        // 写入路径可预期收场（OCE/协议失败皆可），唯独不得是 ObjectDisposedException。
        var process = new ProtocolLevelFakeExifToolProcess();
        var bridge = new ExifToolBridge(process, Config);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        Assert.Equal("13.20", bridge.VersionText);

        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.BlockWrites = release;

        using var wipeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var wipe = Task.Run(() => bridge.WipeMetadataAsync(
            Path.Combine(Path.GetTempPath(), "pp-bridge", "dispose-race.jpg"),
            wipeCts.Token));

        // 等探测块写入真的落在 stdin 闸上（冷启动已有 1 次版本探针写入），避免固定等待赌时序。
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (process.Writes.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(process.Writes.Count >= 2, "探测块写入未取得 stdin 闸：用例前提不成立");

        await bridge.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        bridge.Dispose();

        // 放行在途写入：它的 finally Release 不得因闸已被 Dispose 而抛 ObjectDisposedException。
        release.TrySetResult(true);

        var ex = await Record.ExceptionAsync(() => wipe.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotNull(ex);
        Assert.IsNotType<ObjectDisposedException>(ex);
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

    /// <summary>
    /// 等到条件成立（默认 10 s 上限）。用它定位「协议里的某一步确实已经到了」，而不是靠固定等待赌时序。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"等待超时：{what}。");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>
    /// 等到 fake 累计产出 count 行以前缀开头的 stdout。fake 是先派发后入列，故返回时桥的 stdout
    /// 处理器已消费这些行——用它定位「协议里的某几句确实已经到了」，而不是靠固定等待赌时序。
    /// </summary>
    private static async Task WaitUntilStdoutLineAsync(
        ProtocolLevelFakeExifToolProcess process,
        string prefix,
        int count = 1)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (process.StdoutLines.Count(l => l.StartsWith(prefix, StringComparison.Ordinal)) >= count)
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"未等到 fake 产出第 {count} 行 {prefix}* stdout。");
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



