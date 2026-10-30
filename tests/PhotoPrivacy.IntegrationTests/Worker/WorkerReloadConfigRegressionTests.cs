using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.Worker;

/// <summary>
/// 行为测试：#85 <c>ReloadConfig</c> 重建链路。
///
/// 事故形态：<c>ApplyConfigAsync</c> 重建 <c>FileTaskPipeline</c> 时新建
/// <c>FileProcessedRecordStore</c>，而启动期的旧 store 从未释放——它以
/// <c>FileShare.Read</c> 常驻持有 <c>processed.ndjson</c> 写句柄，新 store 打开同一文件
/// 必然自我冲突（<c>IOException: being used by another process</c>，单实例、任何模式皆然）。
/// 旧实现把该异常吞在 <c>ReloadConfigAsync</c> 里，于是「热重载」在任何模式下都只回 ok
/// 而不生效；服务模式再叠加 EventLog logger 抛异常，就退化成 10 s/15 s 无响应。
///
/// 本类直接驱动真实 worker（dry-run 桥，不 spawn ExifTool），用「重载后热文件夹仍被监视、
/// 新文件仍被处理」区分新旧实现：旧形态重载在新建 store 时就抛（被吞掉），旧 watcher 已停、
/// 新 watcher 从未创建——文件不再被处理，必红。
///
/// 三层覆盖：
/// - handler 级（Theory）：服务与后台（托盘）两种运行形态各自单列（#85 AC「后台管道同方法
///   无回归」），重载链路两种形态共用同一 handler/worker，但响应 <c>Mode</c> 字段按形态回
///   service/background，逐形态断言，防止后台形态被服务形态的断言顺带掩盖；
/// - 端点级（Fact）：后台形态的 ReloadConfig 走真实传输绑定 + accept 循环 + 帧协议往返，
///   不再只是直连 handler——WorkerIpcServerHostedService 的形态端点绑定路径同样被覆盖；
/// - 待机端点级（Fact）：空 <c>hot_folder</c> 的首次配置待机态同样走真实传输 + accept +
///   帧协议（#113 AC1，补齐 #100 关闭时留下的证据缺口）。
///
/// 另含 #112 的失败路径：不可用监控目录（拆卸前拒绝）、拆卸后重建失败（回滚到上一份生效配置），
/// 以及回滚必须重建去抖队列（失败尝试已按新配置换过队列）。三者都必须回 <c>ok:false</c> 且旧目录继续工作。
/// </summary>
public sealed class WorkerReloadConfigRegressionTests
{
    [Fact]
    public async Task EmptyWatchFolder_Should_Stay_Resident_Until_Reload_Starts_Monitoring()
    {
        // 防：默认空目录让 ExecuteAsync 退出或阻断后续 ReloadConfig，导致 Worker 无法完成首次配置（#100）。
        var root = Path.Combine(Path.GetTempPath(), "pp-100-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(string.Empty, audit));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(100);
            Assert.Equal(string.Empty, worker.CurrentWatchDirectory);

            Directory.CreateDirectory(hot);
            await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit));
            await worker.ReloadConfigAsync();

            Assert.Equal(hot, worker.CurrentWatchDirectory);
            Assert.True(
                await WaitForFileAsync(Path.Combine(audit, "processed.ndjson"), TimeSpan.FromSeconds(30)),
                "空目录待机后 ReloadConfig 未启动监控运行时");
            await File.WriteAllTextAsync(Path.Combine(hot, "after-standby.jpg"), "after-standby");
            Assert.True(
                await WaitForProcessedRecordsAsync(Path.Combine(audit, "processed.ndjson"), expectedAtLeast: 1, TimeSpan.FromSeconds(30)),
                "空目录待机后 ReloadConfig 未恢复文件监控处理");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Reload_After_Standby_Should_Sweep_Only_The_Configured_Depth()
    {
        // 防的具体回潮形态：RebuildRuntimeAsync 的首扫把枚举深度写死成 AllDirectories。
        // include_subdirectories=false 时 FSW 只监听顶层，越界扫到的子目录文件却仍进了队列，
        // 而 RuleEngine 不按目录深度过滤——范围外的照片会被真实改写（#100 审查 Major）。
        var root = Path.Combine(Path.GetTempPath(), "pp-100-scope-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var nested = Path.Combine(hot, "nested");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(string.Empty, audit, includeSubdirectories: false));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // 待机期间投放两个文件：顶层在监听范围内，子目录在范围外。
            await File.WriteAllTextAsync(Path.Combine(hot, "in-scope.jpg"), "in-scope");
            await File.WriteAllTextAsync(Path.Combine(nested, "out-of-scope.jpg"), "out-of-scope");

            await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit, includeSubdirectories: false));
            await worker.ReloadConfigAsync();

            var processedPath = Path.Combine(audit, "processed.ndjson");
            // 正向对照：范围内的既有文件必须被处理，否则「范围外文件没记录」只是因为整段扫描没跑。
            Assert.True(
                await WaitForProcessedRecordForAsync(processedPath, "in-scope.jpg", TimeSpan.FromSeconds(30)),
                "待机转有效目录后未扫描监听范围内的既有文件（#100）");

            Assert.False(
                await WaitForProcessedRecordForAsync(processedPath, "out-of-scope.jpg", TimeSpan.FromSeconds(5)),
                "include_subdirectories=false 时重载首扫越界处理了子目录文件（#100）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Reload_With_Unchanged_Watch_Folder_Should_Not_Reprocess_On_Audit_Directory_Change()
    {
        // 防的具体回潮形态：RebuildRuntimeAsync 每次重载都整目录重扫。审计目录一换，
        // 新的 FileProcessedRecordStore 指向新目录、去重记录为空，重扫就把既有文件全部重新处理一遍；
        // fixed_directory 模式下这会覆盖输出目录里用户的改动（#100 审查 Major）。
        var root = Path.Combine(Path.GetTempPath(), "pp-100-audit-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var auditBefore = Path.Combine(hot, "_audit-a");
        var auditAfter = Path.Combine(hot, "_audit-b");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(hot);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, auditBefore));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // 前提：文件在旧审计目录下确实处理过（留下记录），换目录才有「不该再处理一遍」可言。
            await File.WriteAllTextAsync(Path.Combine(hot, "already-done.jpg"), "already-done");
            Assert.True(
                await WaitForProcessedRecordForAsync(
                    Path.Combine(auditBefore, "processed.ndjson"), "already-done.jpg", TimeSpan.FromSeconds(30)),
                "启动态未处理既有文件：用例前提不成立");

            await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, auditAfter));
            await worker.ReloadConfigAsync();

            Assert.True(
                await WaitForFileAsync(Path.Combine(auditAfter, "processed.ndjson"), TimeSpan.FromSeconds(30)),
                "重载后新审计目录未建立处理记录表：重载没有真正生效");

            Assert.False(
                await WaitForProcessedRecordForAsync(
                    Path.Combine(auditAfter, "processed.ndjson"), "already-done.jpg", TimeSpan.FromSeconds(5)),
                "只改审计目录就整目录重扫，已处理文件被重新处理（#100）");

            // 反向对照：目录没换≠监控停了。新文件仍须被处理，防止断言靠「运行时根本没起来」假绿。
            await File.WriteAllTextAsync(Path.Combine(hot, "after-audit-change.jpg"), "after-audit-change");
            Assert.True(
                await WaitForProcessedRecordForAsync(
                    Path.Combine(auditAfter, "processed.ndjson"), "after-audit-change.jpg", TimeSpan.FromSeconds(30)),
                "重载后热文件夹不再处理新文件：ReloadConfig 未生效（#85 回归）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Reload_With_Unchanged_Folder_Should_Keep_The_File_Still_In_The_Debounce_Window()
    {
        // 防的具体回潮形态：RebuildRuntimeAsync 拆卸时把 _debounceQueue 连同去抖窗口内的在途路径一起丢掉。
        // 目录未变的重载不再首扫，新 watcher 的轮询初始快照又把这些文件记成既有文件——
        // 该文件从此永不被上报，隐私元数据静默留存（#85 只护住「已弹队」那一段，管不到窗口内）。
        var root = Path.Combine(Path.GetTempPath(), "pp-100-debounce-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(hot);
        // 5 s 去抖窗口：1.2 s 足以让 FSW 把事件投递进队列，又仍远小于窗口，重载必然落在「在途」区间内。
        await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit, debounceMs: 5000));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var processedPath = Path.Combine(audit, "processed.ndjson");
            // 先等运行时就位再投放：processed.ndjson 由启动期建 store 生成，再静置 500 ms 确保写入落在
            // 启动首扫与 _watcher.Start() 之后——否则可能靠启动扫描入队，把被测的在途窗口掩盖掉。
            Assert.True(
                await WaitForFileAsync(processedPath, TimeSpan.FromSeconds(30)),
                "启动期未创建处理记录表：用例前提不成立");
            await Task.Delay(500);

            await File.WriteAllTextAsync(Path.Combine(hot, "in-flight.jpg"), "in-flight");
            await Task.Delay(1200);

            // 目录与去抖窗口都没变的一次重载。
            await worker.ReloadConfigAsync();
            Assert.Equal(hot, worker.CurrentWatchDirectory);

            Assert.True(
                await WaitForProcessedRecordForAsync(processedPath, "in-flight.jpg", TimeSpan.FromSeconds(30)),
                "去抖窗口内的在途文件被重载拆卸丢弃，此后永不再报（#100）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Reload_Into_Another_Folder_Should_Not_Clean_The_Retired_Folders_In_Flight_File()
    {
        // 防的具体回潮形态：RebuildRuntimeAsync 只要去抖窗口相同就复用队列，不看目录有没有换。
        // 用户从 A 换到 B 后，A 里还压在去抖窗口内的路径会随队列活下来，被 B 的 pipeline 照常清除元数据
        // ——那是用户已经放弃监控的目录（#100 审查）。
        var root = Path.Combine(Path.GetTempPath(), "pp-100-switch-" + Guid.NewGuid().ToString("N"));
        var folderA = Path.Combine(root, "A");
        var folderB = Path.Combine(root, "B");
        var audit = Path.Combine(root, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(folderA, audit, debounceMs: 5000));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var processedPath = Path.Combine(audit, "processed.ndjson");
            Assert.True(
                await WaitForFileAsync(processedPath, TimeSpan.FromSeconds(30)),
                "启动期未创建处理记录表：用例前提不成立");
            await Task.Delay(500);

            // 投进 A：1.2 s 足够事件入队，又仍在 5 s 窗口内——重载发生时它还没被弹队。
            await File.WriteAllTextAsync(Path.Combine(folderA, "retired.jpg"), "retired");
            await Task.Delay(1200);

            await File.WriteAllTextAsync(configPath, BuildConfigJson(folderB, audit, debounceMs: 5000));
            await worker.ReloadConfigAsync();
            Assert.Equal(folderB, worker.CurrentWatchDirectory);

            // 正向对照：B 的运行时确实在跑，否则「A 的文件没记录」只是因为整条链路没起来。
            await File.WriteAllTextAsync(Path.Combine(folderB, "fresh.jpg"), "fresh");
            Assert.True(
                await WaitForProcessedRecordForAsync(processedPath, "fresh.jpg", TimeSpan.FromSeconds(30)),
                "换目录重载后新目录未监控：用例前提不成立");

            Assert.False(
                await WaitForProcessedRecordForAsync(processedPath, "retired.jpg", TimeSpan.FromSeconds(5)),
                "换目录后仍在去抖窗口内的旧目录文件被新运行时清除，越界改了放弃监控的文件（#100）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Reload_That_Widens_The_Scope_Should_Backfill_Existing_Nested_Files()
    {
        // 防的具体回潮形态：只改 include_subdirectories（false→true）被判成「范围没变」，既不整目录补扫、
        // 又被 mtime 兜底跳过——新放开的子目录里既有文件永远不会被处理（#100 审查）。
        var root = Path.Combine(Path.GetTempPath(), "pp-100-scope-flip-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var nested = Path.Combine(hot, "nested");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(hot);
        // 关掉轮询兜底：它的快照恒递归所有子目录（独立票 #109），会把本用例「子目录未被处理」的前提打破。
        await File.WriteAllTextAsync(
            configPath, BuildConfigJson(hot, audit, includeSubdirectories: false, pollingSeconds: 0));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var processedPath = Path.Combine(audit, "processed.ndjson");
            Assert.True(
                await WaitForFileAsync(processedPath, TimeSpan.FromSeconds(30)),
                "启动期未创建处理记录表：用例前提不成立");

            Directory.CreateDirectory(nested);
            var nestedFile = Path.Combine(nested, "pre-existing.jpg");
            await File.WriteAllTextAsync(nestedFile, "pre-existing");
            // mtime 拨到 5 分钟前：排掉「拆卸窗口 mtime 兜底」这条路径，只剩「范围变更补扫」能捞到它。
            File.SetLastWriteTimeUtc(nestedFile, DateTime.UtcNow.AddMinutes(-5));
            await Task.Delay(500);
            Assert.False(
                await WaitForProcessedRecordForAsync(processedPath, "pre-existing.jpg", TimeSpan.FromSeconds(2)),
                "include_subdirectories=false 时子目录文件仍被处理：用例前提不成立");

            // 只放开深度键，监控目录本身不变。
            await File.WriteAllTextAsync(
                configPath, BuildConfigJson(hot, audit, includeSubdirectories: true, pollingSeconds: 0));
            await worker.ReloadConfigAsync();

            Assert.True(
                await WaitForProcessedRecordForAsync(processedPath, "pre-existing.jpg", TimeSpan.FromSeconds(30)),
                "放开 include_subdirectories 后未补扫新放开的子目录，既有文件永远不会被处理（#100）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(RuntimeMode.Service, "service")]
    [InlineData(RuntimeMode.Background, "background")]
    public async Task ReloadConfig_Should_Keep_Watching_And_Processing_After_Repeated_Reloads(
        RuntimeMode runtimeMode, string expectedMode)
    {
        var fixture = await StartProcessingWorkerAsync();

        try
        {
            // 连续两次重载都走真实 IPC 处理器（handler → WorkerRuntimeContext → worker）：
            // 既锁「旧 store 常驻写句柄必须先释放」，也锁「重载真的回 ok 且 Mode 与形态一致」——
            // 旧形态第一次重载的异常被吞，回 ok 但组件已拆；第二次直接失败。
            var handler = CreateHandler(fixture, runtimeMode);

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var (reloadResponse, shouldShutdown) = await handler
                    .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig, "reload-" + attempt), CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(30));

                Assert.True(reloadResponse.Ok, $"第 {attempt} 次重载必须回 ok：{reloadResponse.Message}");
                Assert.False(shouldShutdown);
                Assert.Equal(expectedMode, reloadResponse.Data!.Mode);
            }

            // 重载真正生效的判据：watcher/pipeline 已重建并在跑，新文件继续被处理。
            // 旧形态在此必红——重载抛在 store 构造处（被吞），watcher 停掉后再没重建。
            await AssertReloadKeepsProcessingAsync(fixture, "after-reload.jpg");
        }
        finally
        {
            await StopAndDeleteAsync(fixture);
        }
    }

    /// <summary>
    /// 端点级后台用例（CodeRabbit 对上一版仅 handler 级的 Major 意见）：两条 ReloadConfig
    /// 各走一条真实 IPC 连接（协议一连接一消息），经真实传输绑定 + accept 循环 + 帧协议往返
    /// 到真实 handler/worker，响应按 EOF 定界读回。后台端点的绑定、accept、帧协议、处理链、
    /// 重载生效全部被真实 exercised。
    ///
    /// 不经 WorkerIpcServerHostedService 直接驱动：它把形态端点解析成固定名（Linux
    /// <c>/run/photoprivacy</c> 需 root，CI 绑不了，也不能与真实 worker 抢端点）。因此绑定
    /// 行为用同一工厂的 background 访问策略在临时端点上验证，形态→端点选择映射
    /// （<c>ResolveEndpoint</c> 的 background 分支，与宿主内同一调用）单独逐 OS 断言钉住。
    /// </summary>
    [Fact]
    public async Task ReloadConfig_Through_Background_Endpoint_Should_Reply_And_Keep_Processing()
    {
        var fixture = await StartProcessingWorkerAsync();
        var loopStop = new CancellationTokenSource();
        IIpcTransport? transport = null;
        Task loopTask = Task.CompletedTask;

        try
        {
            var endpoint = ResolveBackgroundTestEndpoint(fixture.Root);

            // 形态→端点选择映射（WorkerIpcServerHostedService 内同一调用的 background 分支）逐 OS 钉住；
            // 兜底失败防止未知 OS 上断言静默跳过。真实固定端点在 CI 无法绑定（Linux /run/photoprivacy
            // 需 root），故绑定行为在临时端点上验证。
            if (OperatingSystem.IsLinux())
            {
                Assert.Equal(WorkerIpcEndpointNames.LinuxBackgroundSocket, IpcTransportFactory.ResolveEndpoint(isServiceMode: false));
            }
            else if (OperatingSystem.IsMacOS())
            {
                Assert.Equal(WorkerIpcEndpointNames.MacBackgroundSocket, IpcTransportFactory.ResolveEndpoint(isServiceMode: false));
            }
            else if (OperatingSystem.IsWindows())
            {
                Assert.Equal(WorkerIpcEndpointNames.BackgroundPipe, IpcTransportFactory.ResolveEndpoint(isServiceMode: false));
            }
            else
            {
                Assert.Fail("未覆盖的 OS：形态→端点选择映射断言被静默跳过");
            }

            transport = IpcTransportFactory.CreateServer(endpoint, isServiceMode: false);
            if (transport is UnixDomainSocketIpcTransport unixTransport)
            {
                // 后台访问策略必须随形态绑定：0600 仅属主可连（service 侧才是 0660 + 组边界）。
                Assert.Equal(UnixSocketAccessMode.BackgroundOnly, unixTransport.AccessMode);
            }

            var handler = CreateHandler(fixture, RuntimeMode.Background);

            var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
            // 与宿主同序：先绑定端点再进 accept 循环（两个传输的 ListenAsync 都是非阻塞绑定）。
            await transport.ListenAsync(loopStop.Token);
            loopTask = loop.RunAsync(transport, handler.HandleAsync, requestShutdown: () => { }, loopStop.Token);

            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var response = await RequestOverTransportAsync(
                    transport, new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig, "reload-endpoint-" + attempt));
                Assert.True(response.Ok, $"第 {attempt} 次重载必须经后台端点回 ok：{response.Message}");
                Assert.Equal("background", response.Data!.Mode);
                Assert.Equal(WorkerIpcProtocol.Version, response.V);
            }

            // 重载真正生效的判据与 handler 级用例一致：watcher/pipeline 已重建并在跑。
            await AssertReloadKeepsProcessingAsync(fixture, "after-reload-endpoint.jpg");
        }
        finally
        {
            loopStop.Cancel();
            try
            {
                await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // 循环停不下来只说明停机路径有缺陷，不得掩盖上面的断言失败。
            }

            if (transport is not null)
            {
                await transport.DisposeAsync();
            }

            await StopAndDeleteAsync(fixture);
        }
    }

    [Fact]
    public async Task Reload_With_Unusable_Watch_Folder_Should_Keep_The_Old_Folder_Monitored()
    {
        // 防的具体回潮形态：重载到「指向已存在文件的目录」时抛错，旧 watcher 已停、新运行时未建，
        // worker 活着却不再监控原目录——此后新文件永远不被处理（#112 票面场景）。
        // 该错误目前在应用前的校验步骤（LoadEffectiveConfig 建目录）就被拒，尚未进入拆卸；
        // 本用例同时锁「拒绝后旧运行时原样保留」与「IPC 不静默回 ok」。
        // 关联 issue #112。
        var fixture = await StartProcessingWorkerAsync();

        try
        {
            // 目标监控目录是个文件：Directory.CreateDirectory 必抛。
            var blocker = Path.Combine(fixture.Root, "blocker-file");
            await File.WriteAllTextAsync(blocker, "not-a-directory");
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "config.json"),
                BuildConfigJson(blocker, fixture.AuditDirectory));

            var handler = CreateHandler(fixture, RuntimeMode.Background);
            var (response, shouldShutdown) = await handler
                .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig, "reload-bad-folder"), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.False(response.Ok, "重载到不可用目录必须回 ok:false，不得静默回 ok（#112）");
            Assert.False(shouldShutdown);
            Assert.Contains("配置验证失败", response.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(fixture.Hot, fixture.Worker.CurrentWatchDirectory);

            // 旧目录仍被监控：新文件必须继续被处理，否则「回 ok:false」只是假象。
            await AssertReloadKeepsProcessingAsync(fixture, "after-bad-folder-reload.jpg");
        }
        finally
        {
            await StopAndDeleteAsync(fixture);
        }
    }

    [Fact]
    public async Task Reload_That_Fails_After_Teardown_Should_Roll_Back_And_Keep_The_Old_Folder_Monitored()
    {
        // 防的具体回潮形态：拆卸（停 watcher、释放 store/audit）之后重建抛错，异常一路抛到 IPC，
        // 旧运行时没有建回来——worker 活着但不再监控原目录，要等下一次成功重载或重启才恢复（#112）。
        // 用「审计目录里放一个名为 processed.ndjson 的目录」确定性触发拆卸后失败：校验与建目录都通过，
        // FileProcessedRecordStore 打开 processed.ndjson 时才抛，正好落在拆卸之后。
        // 关联 issue #112。
        var fixture = await StartProcessingWorkerAsync();

        try
        {
            var brokenAudit = Path.Combine(fixture.Root, "audit-broken");
            Directory.CreateDirectory(Path.Combine(brokenAudit, "processed.ndjson"));
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "config.json"),
                BuildConfigJson(fixture.Hot, brokenAudit));

            var handler = CreateHandler(fixture, RuntimeMode.Background);
            var (response, shouldShutdown) = await handler
                .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig, "reload-teardown-fail"), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.False(response.Ok, "拆卸后重建失败必须回 ok:false，不得静默回 ok（#112）");
            Assert.False(shouldShutdown);
            Assert.StartsWith("reload_failed:", response.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.Contains("已回滚", response.Message ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(fixture.Hot, fixture.Worker.CurrentWatchDirectory);

            // 回滚判据：旧目录继续处理新文件。未回滚时 watcher 已停、pipeline 已拆，文件不会被处理。
            await File.WriteAllTextAsync(Path.Combine(fixture.Hot, "after-failed-reload.jpg"), "after-failed-reload");
            Assert.True(
                await WaitForProcessedRecordForAsync(
                    fixture.ProcessedPath, "after-failed-reload.jpg", TimeSpan.FromSeconds(30)),
                "重载失败后旧目录不再处理新文件：worker 停在「活着但不再监控」形态（#112）");
        }
        finally
        {
            await StopAndDeleteAsync(fixture);
        }
    }

    [Fact]
    public async Task Reload_That_Fails_After_Changing_The_Debounce_Window_Should_Reprocess_The_In_Flight_File()
    {
        // 防的具体回潮形态：失败尝试按新 DebounceMs 换掉去抖队列后抛错，回滚直接复用这条队列——
        // 回滚后的去抖窗口仍是新配置的值，旧窗口里的在途路径既不补扫也不在新队列里，文件静默漏处理
        // （#112 回滚路径上的 #85 形态）。前提：文件的 mtime 早于回滚补扫的下限（拆卸起点 - 2 s），
        // 只有「回滚重建队列 + 全量补扫」能把它捞回来。
        // 关联 issue #112。
        var fixture = await StartProcessingWorkerAsync(debounceMs: 8000);

        try
        {
            await Task.Delay(500);

            var inFlight = Path.Combine(fixture.Hot, "in-flight-before-failed-reload.jpg");
            await File.WriteAllTextAsync(inFlight, "in-flight");
            // mtime 拨到 10 s 前：排除回滚的 handoff 余量窗口，只剩全量补扫能捞回它。
            File.SetLastWriteTimeUtc(inFlight, DateTime.UtcNow.AddSeconds(-10));
            // 1.2 s 足够事件进入旧队列，又远小于 8 s 窗口——重载发生时它仍未处理。
            await Task.Delay(1200);
            Assert.False(
                HasProcessedRecordFor(fixture.ProcessedPath, "in-flight-before-failed-reload.jpg"),
                "在途文件在重载前已被处理：用例前提不成立");

            var brokenAudit = Path.Combine(fixture.Root, "audit-broken");
            Directory.CreateDirectory(Path.Combine(brokenAudit, "processed.ndjson"));
            await File.WriteAllTextAsync(
                Path.Combine(fixture.Root, "config.json"),
                BuildConfigJson(fixture.Hot, brokenAudit, debounceMs: 100));

            var handler = CreateHandler(fixture, RuntimeMode.Background);
            var (response, shouldShutdown) = await handler
                .HandleAsync(new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig, "reload-debounce-change"), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(60));

            Assert.False(response.Ok, "拆卸后重建失败必须回 ok:false，不得静默回 ok（#112）");
            Assert.False(shouldShutdown);
            Assert.StartsWith("reload_failed:", response.Message ?? string.Empty, StringComparison.Ordinal);

            Assert.True(
                await WaitForProcessedRecordForAsync(
                    fixture.ProcessedPath,
                    "in-flight-before-failed-reload.jpg",
                    TimeSpan.FromSeconds(40)),
                "回滚复用了失败尝试按新配置重建的去抖队列，旧窗口内的在途文件没有被补扫（#112）");
        }
        finally
        {
            await StopAndDeleteAsync(fixture);
        }
    }

    /// <summary>
    /// 待机端点级用例（#113 AC1，补 #100 关闭时留下的证据缺口）：默认 <c>hot_folder: ""</c> 的首次配置
    /// 待机态此前只有两层覆盖——handler 级假运行时（<c>WorkerIpcResidentStabilityTests</c>）与进程内
    /// <c>worker.StartAsync</c>（<see cref="EmptyWatchFolder_Should_Stay_Resident_Until_Reload_Starts_Monitoring"/>），
    /// 真实传输绑定 + accept 循环 + 帧协议往返在待机态从没被走过，而 #100 的实机形态正是「UI 连不上、
    /// 只剩管道超时（os error 2）」。
    ///
    /// 防的具体回潮形态（本用例的射程就是「待机态下的 IPC 连通」，不认领别的）：
    /// ① 待机态走不通「绑定 → accept 环 → 帧协议 → handler 回包」这条链，或回包把待机读成断线
    ///    （首拍 GetStatus 往返直接红）；
    /// ② 待机态只服务得了一拍（UI 心跳是每 1 s 一次 GetStatus、一条连接一个请求，见 monitor.rs 的
    ///    Design(#49)；第二拍同方法往返 + accept 环未退出挡这条回潮）。
    /// 两条射程的边界与同文件端点用例相同：绑定与 accept 环由用例按宿主同序自己搭，
    /// <c>WorkerIpcServerHostedService</c> 的形态端点接线、以及「worker 在待机就退出」这种
    /// 不影响回包的形态，都不在本用例的射程内（后者由
    /// <see cref="EmptyWatchFolder_Should_Stay_Resident_Until_Reload_Starts_Monitoring"/> 的正向证据把关）。
    ///
    /// 前提屏障：首拍前先等 worker 走完配置加载（<c>LoadEffectiveConfig</c> 建的审计目录出现为止）。
    /// <c>CurrentWatchDirectory</c> 在「配置还没加载」时同样回空串，没有这道屏障，回包的空目录
    /// 可能只是「什么都还没发生」。
    ///
    /// 覆盖边界（勿当作 #100 崩溃路径的替身）：<c>AppConfigValidator.Validate</c> 有两道早退——
    /// <c>dry_run</c> 一条、空 <c>hot_folder</c> 一条（后者正是 #100 的改动），任一命中就走不到
    /// exiftool 路径存在性、quarantine/audit 目录这些后续校验；本夹具两条都命中，所以就算把
    /// <c>dry_run</c> 改成 false 也带不进那些校验。那条「非 dry-run 的待机链路」归 #108 裁定
    /// （裸机发布包不含 ExifTool，空监控目录又让校验整体 fail-open），本票不替它把行为钉进 CI 门禁。
    /// </summary>
    [Fact]
    public async Task Standby_GetStatus_Through_Background_Endpoint_Should_Reply_With_Empty_Watch_Directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-113-" + Guid.NewGuid().ToString("N"));
        var audit = Path.Combine(root, "audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);
        // 监控目录留空（发版包 config/config.json 的出厂形态）。审计目录给真实路径但**不**预建：
        // LoadEffectiveConfig 在返回配置前会把已配置的 hot/audit/quarantine 目录建出来，
        // 于是「审计目录出现」成了 worker 已走完配置加载的正向屏障（见 try 开头的 Wait）。
        // 屏障只证明配置加载走完，不证明「已进待机」；回包也只证明连通与空目录。
        // 「worker 是否真的待在待机里」由同文件上方的兄弟用例以正向判据承担。
        await File.WriteAllTextAsync(configPath, BuildConfigJson(string.Empty, audit));

        // 夹具自检：worker 启动路径对配置校验失败会直接 Environment.Exit(1)（杀死整个测试进程），
        // 所以先在测试侧校验一遍——配置夹具漂移要以断言失败暴露，而不是让 CI 只看到进程消失。
        var parsed = AppConfigLoader.Load(configPath);
        AppConfigValidator.Validate(parsed);
        Assert.Equal(string.Empty, parsed.Watch.HotFolder);
        Assert.True(parsed.ExifTool.DryRun, "夹具必须是 dry-run：否则会去 spawn ExifTool");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var runtimeControl = new RuntimeControl();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            runtimeControl);

        await worker.StartAsync(CancellationToken.None);

        var loopStop = new CancellationTokenSource();
        IIpcTransport? transport = null;
        Task loopTask = Task.CompletedTask;

        try
        {
            // 就绪屏障：worker 的 CurrentWatchDirectory 在「配置还没加载」与「已进待机」两种状态下
            // 都回空串，少了这一步首拍可能靠空串假绿。审计目录由 LoadEffectiveConfig 建出来，
            // 它出现只证明「配置加载走完了」——本用例证的是这条链通不通、回包给的是不是空目录，
            // 「worker 是否真待在待机里」不在射程（见上面的射程声明），由兄弟用例的正向判据把关。
            Assert.True(
                await WaitForDirectoryAsync(audit, TimeSpan.FromSeconds(30)),
                "worker 未走完配置加载（审计目录没被建出来）：用例前提不成立");

            transport = IpcTransportFactory.CreateServer(ResolveBackgroundTestEndpoint(root), isServiceMode: false);
            // 与生产宿主同接线：watchDirectory 直接取 worker.CurrentWatchDirectory，
            // 待机态的「空目录 + 已连接」必须由这条真实回包链路呈现给 UI。
            var handler = new WorkerIpcRequestHandler(
                new WorkerRuntimeContext(
                    runtimeControl,
                    worker,
                    () => worker.CurrentWatchDirectory,
                    RuntimeMode.Background,
                    () => audit),
                NullLogger<WorkerIpcRequestHandler>.Instance);

            var loop = new WorkerIpcServerLoop(NullLogger<WorkerIpcServerLoop>.Instance);
            // 与宿主同序：先绑定端点再进 accept 循环。
            await transport.ListenAsync(loopStop.Token);
            loopTask = loop.RunAsync(transport, handler.HandleAsync, requestShutdown: () => { }, loopStop.Token);

            var request = new WorkerIpcRequest(WorkerIpcMethods.GetStatus, "standby-status");
            var status = await RequestOverTransportAsync(transport, request);

            Assert.True(status.Ok, $"待机态 GetStatus 必须经后台端点回 ok：{status.Message}");
            Assert.Equal(string.Empty, status.Data!.WatchDirectory);
            Assert.Equal("background", status.Data.Mode);
            Assert.Equal(WorkerIpcProtocol.Version, status.V);

            // UI 的心跳是「每 1 s 一次 GetStatus、一条连接一个请求」的轮询
            // （monitor.rs 的 Design(#49)：探针固定 GetStatus，不用 Ping）——第二拍必须是同方法。
            var secondBeatRequest = new WorkerIpcRequest(WorkerIpcMethods.GetStatus, "standby-status-2");
            var secondBeat = await RequestOverTransportAsync(transport, secondBeatRequest);
            Assert.True(secondBeat.Ok, "待机态第二拍 GetStatus 必须仍被服务");
            Assert.Equal(string.Empty, secondBeat.Data!.WatchDirectory);
            Assert.False(loopTask.IsCompleted, "accept 环不得在待机首拍之后退出");
        }
        finally
        {
            loopStop.Cancel();
            try
            {
                await loopTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // 循环停不下来只说明停机路径有缺陷，不得掩盖上面的断言失败。
            }

            if (transport is not null)
            {
                await transport.DisposeAsync();
            }

            // 停机必须有界，但本用例不在这里断言它停得下来：finally 里抛出的异常会顶掉 try 内的真实
            // 断言失败，把「连通性没验过」伪装成「只是停不下来」。
            // 仍在待机态就 StopAsync 会挂住是 #117 登记的独立缺陷；基类 StopAsync 只受调用方 token 约束，
            // 挂住会一路吃到 test.runsettings 的 1 h TestSessionTimeout——CI 只剩一条无名的红。
            // 5 s 有界停机：拿不到结果也只降级继续，ExecuteAsync 那条任务留给进程退出收口。
            // #117 修好后，把这里改回「断言 StopAsync 在预算内完成」（那条红测挂在 #117）。
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(stopCts.Token);
            TryDelete(root);
        }
    }

    /// <summary>
    /// 端点级用例的临时后台端点。Windows 管道名是内核对象名。Unix socket 落点：Linux 用夹具临时目录
    /// （固定路径 <c>/run/photoprivacy</c> 需 root，CI 绑不了）；macOS 的 <c>sun_path</c> 上限 104 字节，
    /// 夹具目录路径太长，改用临时目录下的短文件名（<c>GetTempPath()</c> 约 49 字节 + 约 21 字节 &lt; 104），
    /// socket 文件由 <c>transport.DisposeAsync</c>（端点属主）负责删除。
    /// </summary>
    private static string ResolveBackgroundTestEndpoint(string root)
        => OperatingSystem.IsWindows()
            ? "pp-bg-" + Guid.NewGuid().ToString("N")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Path.GetTempPath(), "pp-bg-" + Guid.NewGuid().ToString("N")[..8] + ".sock")
                : Path.Combine(root, "worker-background.sock");

    /// <summary>
    /// 经真实传输做一次「一连接一消息」的帧协议往返：写请求行，读以连接关闭（EOF）定界的响应行
    /// （响应无结尾换行，读侧复用唯一的有界行读实现）。
    /// </summary>
    private static async Task<WorkerIpcResponse> RequestOverTransportAsync(IIpcTransport transport, WorkerIpcRequest request)
    {
        await using var stream = await transport
            .ConnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));
        var requestBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(request, WorkerIpcJsonContext.Default.WorkerIpcRequest) + "\n");
        await stream.WriteAsync(requestBytes, CancellationToken.None);
        await stream.FlushAsync(CancellationToken.None);

        var line = await IpcLineReader
            .ReadBoundedLineAsync(stream, maxBytes: 64 * 1024, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(line);

        var response = JsonSerializer.Deserialize(line!, WorkerIpcJsonContext.Default.WorkerIpcResponse);
        Assert.NotNull(response);
        Assert.Equal(request.Id, response!.Id);
        return response;
    }

    private static WorkerIpcRequestHandler CreateHandler(ReloadFixture fixture, RuntimeMode runtimeMode)
    {
        var runtime = new WorkerRuntimeContext(
            new RuntimeControl(),
            fixture.Worker,
            () => fixture.Hot,
            runtimeMode,
            () => fixture.AuditDirectory);
        return new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);
    }

    /// <summary>重载真正生效的判据：重载后写入新文件，等处理记录继续增长（旧实现必红）。</summary>
    private static async Task AssertReloadKeepsProcessingAsync(ReloadFixture fixture, string fileName)
    {
        await File.WriteAllTextAsync(Path.Combine(fixture.Hot, fileName), fileName);
        Assert.True(
            await WaitForProcessedRecordsAsync(fixture.ProcessedPath, expectedAtLeast: 2, TimeSpan.FromSeconds(30)),
            "重载后热文件夹不再处理文件：ReloadConfig 没有真正生效（#85）");
    }

    private sealed record ReloadFixture(
        string Root, string Hot, string AuditDirectory, string ProcessedPath, MetadataCleanerWorker Worker);

    /// <summary>
    /// 启动真实 worker（dry-run）并证明「启动态确实在处理文件」：写 baseline.jpg 后等
    /// processed.ndjson 出现至少 1 条记录。此后测试续做重载断言；结束统一走
    /// <see cref="StopAndDeleteAsync"/>。
    /// </summary>
    private static async Task<ReloadFixture> StartProcessingWorkerAsync(int debounceMs = 100)
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-85-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");

        Directory.CreateDirectory(hot);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit, debounceMs: debounceMs));

        // 夹具自检：worker 的启动路径对配置校验失败会直接 Environment.Exit(1)（杀死整个测试进程），
        // 所以先在测试侧校验一遍——配置夹具漂移要以断言失败暴露，而不是让 CI 只看到进程消失。
        var parsed = AppConfigLoader.Load(configPath);
        AppConfigValidator.Validate(parsed);
        Assert.True(parsed.ExifTool.DryRun, "夹具必须是 dry-run：否则会去 spawn ExifTool");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();

        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        await worker.StartAsync(CancellationToken.None);

        try
        {
            // ExecuteAsync 建 pipeline 时创建 processed.ndjson；见到该文件即启动期 store 已就位。
            var processedPath = Path.Combine(audit, "processed.ndjson");
            Assert.True(
                await WaitForFileAsync(processedPath, TimeSpan.FromSeconds(30)),
                $"启动期未创建 {processedPath}（worker 可能未能完成初始化）");

            // 基线：启动态确实在处理文件（1 条处理记录）。
            await File.WriteAllTextAsync(Path.Combine(hot, "baseline.jpg"), "baseline");
            Assert.True(
                await WaitForProcessedRecordsAsync(processedPath, expectedAtLeast: 1, TimeSpan.FromSeconds(30)),
                "启动态未处理热文件夹里的文件：用例前提不成立");
            Assert.Equal(hot, worker.CurrentWatchDirectory);

            return new ReloadFixture(root, hot, audit, processedPath, worker);
        }
        catch
        {
            // 夹具未返回，调用方的 finally 够不着：就绪断言失败时就地清理，原样重抛。
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
            throw;
        }
    }

    private static async Task StopAndDeleteAsync(ReloadFixture fixture)
    {
        await fixture.Worker.StopAsync(CancellationToken.None);
        TryDelete(fixture.Root);
    }

    private static string BuildConfigJson(
        string hotFolder,
        string auditDirectory,
        bool includeSubdirectories = true,
        int debounceMs = 100,
        int pollingSeconds = 1)
    {
        // 全量键位对齐 config.sample.json；dry_run=true 让 validator 走早退分支
        // （不需要真实 exiftool.exe 与 exiftool_files 目录）。
        var payload = new
        {
            schema_version = 1,
            exiftool = new
            {
                path = "exiftool.exe",
                enable_windows_long_path = true,
                enable_large_file_support = true,
                dry_run = true,
                extra_exiftool_args = Array.Empty<string>(),
                stay_open_pool_size = 1,
                max_parallel_drain = 1
            },
            watch = new
            {
                hot_folder = hotFolder,
                include_subdirectories = includeSubdirectories,
                debounce_ms = debounceMs,
                internal_buffer_size = 65536,
                auto_excluded_directories = Array.Empty<string>(),
                // 轮询兜底（默认 1 s）：CI 上 FSW 事件可能丢失，轮询让「文件被处理」这类断言可重复；
                // 传 0 关掉，用于只让 FSW 这一条通路生效的用例。
                polling_interval_seconds = pollingSeconds
            },
            rules = new
            {
                allowed_extensions = new[] { ".jpg", ".jpeg" },
                excluded_patterns = new[] { "~$*", "*.tmp" },
                output_mode = "same_as_source",
                output_directory = string.Empty
            },
            retry = new { max_attempts = 3, backoff_seconds = new[] { 1, 3, 10 } },
            backup = new
            {
                enabled = false,
                directory = string.Empty,
                suffix = ".bak",
                max_size_mb = 100,
                retain_days = 30
            },
            quarantine = new { enabled = false, directory = string.Empty },
            audit = new
            {
                log_directory = auditDirectory,
                retain_days = 30,
                diagnostic_mode = false,
                log_level = "info"
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static Task<bool> WaitForFileAsync(string path, TimeSpan budget)
        => WaitForPresenceAsync(() => File.Exists(path), budget);

    /// <summary>待机用例的就绪屏障用：审计目录由 <c>LoadEffectiveConfig</c> 建出来，出现即说明配置已加载。</summary>
    private static Task<bool> WaitForDirectoryAsync(string path, TimeSpan budget)
        => WaitForPresenceAsync(() => Directory.Exists(path), budget);

    private static async Task<bool> WaitForPresenceAsync(Func<bool> exists, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (exists())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return exists();
    }

    /// <summary>
    /// 等处理记录条数达到预期。<c>processed.ndjson</c> 由 store 以 <c>FileAccess.Write</c> +
    /// <c>FileShare.Read</c> 常驻持有，读侧必须用 <c>FileShare.ReadWrite</c> 才能真正共享
    /// （Windows 下 <c>File.ReadAllLines</c> 的 <c>FileShare.Read</c> 与写句柄冲突，必抛共享冲突）。
    /// 每成功处理一个文件追加一行。
    /// </summary>
    private static async Task<bool> WaitForProcessedRecordsAsync(string processedPath, int expectedAtLeast, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (CountProcessedRecords(processedPath) >= expectedAtLeast)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return CountProcessedRecords(processedPath) >= expectedAtLeast;
    }

    private static int CountProcessedRecords(string processedPath)
    {
        if (!File.Exists(processedPath))
        {
            return 0;
        }

        try
        {
            using var stream = new FileStream(
                processedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            // 只认「已写完整的 JSON 行」（以 } 结尾）：写入瞬间可能读到半行，按行计数会提前判绿。
            var count = 0;
            while (reader.ReadLine() is { } line)
            {
                if (line.TrimEnd().EndsWith('}'))
                {
                    count++;
                }
            }

            return count;
        }
        catch (FileNotFoundException)
        {
            // 文件在 Exists 与 Open 之间被 Compact 换掉：下一轮再读。
            return 0;
        }
    }

    /// <summary>
    /// 等「指定文件名的处理记录」出现。记录键是 <c>绝对路径|长度|LastWriteTimeUtc.Ticks</c> 明文，
    /// 按文件名子串匹配即可绕开 Windows 路径在 JSON 行里的反斜杠转义。
    /// </summary>
    private static async Task<bool> WaitForProcessedRecordForAsync(string processedPath, string fileName, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (HasProcessedRecordFor(processedPath, fileName))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return HasProcessedRecordFor(processedPath, fileName);
    }

    private static bool HasProcessedRecordFor(string processedPath, string fileName)
    {
        if (!File.Exists(processedPath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                processedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            // 与 CountProcessedRecords 同口径：只认写完整的 JSON 行，半行不计入。
            while (reader.ReadLine() is { } line)
            {
                if (line.TrimEnd().EndsWith('}') && line.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (FileNotFoundException)
        {
            // 文件在 Exists 与 Open 之间被 Compact 换掉：下一轮再读。
            return false;
        }
    }

    private static void TryDelete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best-effort：FSW 句柄释放有延迟，临时目录残留不影响断言。
        }
    }

    private sealed class NullApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
