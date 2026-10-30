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
/// 两层覆盖：
/// - handler 级（Theory）：服务与后台（托盘）两种运行形态各自单列（#85 AC「后台管道同方法
///   无回归」），重载链路两种形态共用同一 handler/worker，但响应 <c>Mode</c> 字段按形态回
///   service/background，逐形态断言，防止后台形态被服务形态的断言顺带掩盖；
/// - 端点级（Fact）：后台形态的 ReloadConfig 走真实传输绑定 + accept 循环 + 帧协议往返，
///   不再只是直连 handler——WorkerIpcServerHostedService 的形态端点绑定路径同样被覆盖。
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
            // Windows 管道名是内核对象名。Unix socket 落点：Linux 用夹具临时目录（固定路径
            // /run/photoprivacy 需 root）；macOS 的 sun_path 上限 104 字节，夹具目录路径太长，
            // 改用临时目录下的短文件名（GetTempPath() 约 49 字节 + 约 21 字节 < 104），
            // socket 文件由 transport.DisposeAsync（端点属主）负责删除。
            var endpoint = OperatingSystem.IsWindows()
                ? "pp85-bg-" + Guid.NewGuid().ToString("N")
                : OperatingSystem.IsMacOS()
                    ? Path.Combine(Path.GetTempPath(), "pp85-bg-" + Guid.NewGuid().ToString("N")[..8] + ".sock")
                    : Path.Combine(fixture.Root, "worker-background.sock");

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
                var request = new WorkerIpcRequest(WorkerIpcMethods.ReloadConfig, "reload-endpoint-" + attempt);

                await using var stream = await transport
                    .ConnectAsync(TimeSpan.FromSeconds(10), CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(30));
                var requestBytes = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(request, WorkerIpcJsonContext.Default.WorkerIpcRequest) + "\n");
                await stream.WriteAsync(requestBytes, CancellationToken.None);
                await stream.FlushAsync(CancellationToken.None);

                // 响应无结尾换行、以连接关闭（EOF）定界；读侧复用唯一的有界行读实现。
                var line = await IpcLineReader
                    .ReadBoundedLineAsync(stream, maxBytes: 64 * 1024, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(30));
                Assert.NotNull(line);

                var response = JsonSerializer.Deserialize(line!, WorkerIpcJsonContext.Default.WorkerIpcResponse);
                Assert.NotNull(response);
                Assert.True(response!.Ok, $"第 {attempt} 次重载必须经后台端点回 ok：{response.Message}");
                Assert.Equal("background", response.Data!.Mode);
                Assert.Equal(WorkerIpcProtocol.Version, response.V);
                Assert.Equal(request.Id, response.Id);
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
    private static async Task<ReloadFixture> StartProcessingWorkerAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-85-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");

        Directory.CreateDirectory(hot);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit));

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

    private static string BuildConfigJson(string hotFolder, string auditDirectory)
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
                include_subdirectories = true,
                debounce_ms = 100,
                internal_buffer_size = 65536,
                auto_excluded_directories = Array.Empty<string>(),
                // 1 s 轮询兜底：CI 上 FSW 事件可能丢失，轮询让「文件被处理」这条断言可重复。
                polling_interval_seconds = 1
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

    private static async Task<bool> WaitForFileAsync(string path, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return File.Exists(path);
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
