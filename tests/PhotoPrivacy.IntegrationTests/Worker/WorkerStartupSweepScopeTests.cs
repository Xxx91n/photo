using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;

namespace PhotoPrivacy.IntegrationTests.Worker;

/// <summary>
/// 行为测试：启动先扫一遍的监控范围与时序（#109）。
///
/// 事故形态：重载时先扫一遍已在 #100/#107 修成「先起 watcher、深度取 include_subdirectories」，
/// 启动先扫一遍却原样留着两处旧实现——枚举深度写死 <c>SearchOption.AllDirectories</c>，且先扫后起
/// watcher。同一进程两条路径规则分叉：include_subdirectories=false 时启动先扫一遍把监听范围之外的
/// 子目录照片真实改写元数据；扫描期间新建的文件落进轮询补漏扫的初始快照，从此永不上报。
///
/// 本类直接驱动真实 worker（dry-run 桥，不 spawn ExifTool），用启动先扫一遍的入队结果区分新旧实现。
/// </summary>
public sealed class WorkerStartupSweepScopeTests
{
    /// <summary>启动先扫一遍在 include_subdirectories=false 时只处理顶层既有文件，子目录文件两条通路都不进队列（#109 AC1）。</summary>
    [Fact]
    public async Task Startup_Should_Sweep_Only_The_Configured_Depth_When_Subdirectories_Are_Excluded()
    {
        // 要防的老毛病：启动先扫一遍把枚举深度写死成 AllDirectories。include_subdirectories=false 时
        // FSW 只监听顶层、RuleEngine 也不按目录深度过滤，越界扫到的子目录文件会被真实改写（#109）。
        var root = Path.Combine(Path.GetTempPath(), "pp-109-startup-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var nested = Path.Combine(hot, "nested");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit, includeSubdirectories: false));
        AssertViableFixtureConfig(configPath);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["config"] = configPath })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        // 启动前投放两个文件：顶层在监听范围内，子目录在范围外——两者都是启动先扫一遍的枚举对象。
        // 必须先于 StartAsync：否则 watcher/轮询事件能满足正向断言，启动先扫一遍被删也可能假绿（CodeRabbit）。
        await File.WriteAllTextAsync(Path.Combine(hot, "in-scope.jpg"), "in-scope");
        await File.WriteAllTextAsync(Path.Combine(nested, "out-of-scope.jpg"), "out-of-scope");

        await worker.StartAsync(CancellationToken.None);
        try
        {
            // 启动后另投一个子目录文件：轮询补漏扫与 FSW 都不得把它报进队列（#109 的补漏扫深度裁定）。
            await File.WriteAllTextAsync(Path.Combine(nested, "post-start-nested.jpg"), "post-start");

            var processedPath = Path.Combine(audit, "processed.ndjson");
            // 正向对照：范围内的既有文件必须被启动先扫一遍处理，否则「范围外文件没记录」只是因为整段扫描没跑。
            Assert.True(
                await WaitForProcessedRecordForAsync(processedPath, "in-scope.jpg", TimeSpan.FromSeconds(30)),
                "启动先扫一遍没有处理监听范围内的既有文件：用例前提不成立");

            Assert.False(
                await WaitForProcessedRecordForAsync(processedPath, "out-of-scope.jpg", TimeSpan.FromSeconds(5)),
                "include_subdirectories=false 时启动先扫一遍越界处理了子目录文件（#109）");
            Assert.False(
                await WaitForProcessedRecordForAsync(processedPath, "post-start-nested.jpg", TimeSpan.FromSeconds(5)),
                "include_subdirectories=false 时轮询补漏扫越界上报了子目录新文件（#109）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    /// <summary>
    /// once 模式（<c>--mode cli --once true</c>）的守护（#109 AC3）：本票改的正是 once 枚举前后的代码，
    /// 而 CI 的 dotnet test 规则排除 Smoke（once 此前只有 Smoke 的真实进程用例背书），一旦老毛病再犯主测试 job 不红。
    /// 本用例在进程内驱动同一条 ExecuteAsync once 分支，锁两件事：先枚举后 drain 的时序仍然成立
    /// （既有文件被处理），且枚举深度跟随 include_subdirectories（范围外文件不进 once 模式的扫描）。
    /// </summary>
    [Fact]
    public async Task Cli_Once_Should_Sweep_Before_Draining_And_Respect_The_Configured_Depth()
    {
        // 要防的老毛病：once 分支的枚举深度写死成 AllDirectories，或时序被打乱导致既有文件不被
        // once 清理（#109；CI 规则排除 Smoke，once 路径只有本用例守护）。
        var root = Path.Combine(Path.GetTempPath(), "pp-109-once-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var nested = Path.Combine(hot, "nested");
        var audit = Path.Combine(hot, "_audit");
        var configPath = Path.Combine(root, "config.json");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(configPath, BuildConfigJson(hot, audit, includeSubdirectories: false));
        AssertViableFixtureConfig(configPath);

        await File.WriteAllTextAsync(Path.Combine(hot, "once-in-scope.jpg"), "in-scope");
        await File.WriteAllTextAsync(Path.Combine(nested, "once-out-of-scope.jpg"), "out-of-scope");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["config"] = configPath,
                ["once"] = "true"
            })
            .Build();
        var worker = new MetadataCleanerWorker(
            configuration,
            NullLogger<MetadataCleanerWorker>.Instance,
            new NullApplicationLifetime(),
            new RuntimeControl());

        try
        {
            await worker.StartAsync(CancellationToken.None);

            var processedPath = Path.Combine(audit, "processed.ndjson");
            Assert.True(
                await WaitForProcessedRecordForAsync(processedPath, "once-in-scope.jpg", TimeSpan.FromSeconds(30)),
                "once 模式未在退出前清理既有文件：先枚举后 drain 的时序被破坏（#109 AC3）");

            Assert.False(
                await WaitForProcessedRecordForAsync(processedPath, "once-out-of-scope.jpg", TimeSpan.FromSeconds(5)),
                "once 模式枚举越界处理了子目录文件（#109）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
    }

    /// <summary>
    /// 夹具自检：worker 启动路径对配置校验失败会直接 <c>Environment.Exit(1)</c>（杀死整个测试进程），
    /// 所以先在测试侧校验一遍——配置夹具漂移要以断言失败暴露，而不是让 CI 只看到进程消失。
    /// </summary>
    private static void AssertViableFixtureConfig(string configPath)
    {
        var parsed = AppConfigLoader.Load(configPath);
        AppConfigValidator.Validate(parsed);
        Assert.True(parsed.ExifTool.DryRun, "夹具必须是 dry-run：否则会去 spawn ExifTool");
        Assert.False(parsed.Watch.IncludeSubdirectories, "夹具必须关闭 include_subdirectories：否则范围断言无从谈起");
    }

    private static string BuildConfigJson(string hotFolder, string auditDirectory, bool includeSubdirectories)
    {
        // 全量键位对齐 config.sample.json；dry_run=true 让 validator 走早退分支（不需要真实 exiftool）。
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
                debounce_ms = 100,
                internal_buffer_size = 65536,
                auto_excluded_directories = Array.Empty<string>(),
                // 轮询补漏扫（默认 1 s）：CI 上 FSW 事件可能丢失，轮询让「范围内文件被处理」可重复；
                // 它的快照深度现在与 FSW 同源（#109），子目录文件两条通路都到不了处理队列。
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
            quarantine = new
            {
                enabled = false,
                directory = string.Empty
            },
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

    /// <summary>轮询等待指定文件名的处理记录出现（CI 上处理完成时点不定），超时回最终观测值。</summary>
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

    /// <summary>单次读取 processed.ndjson 判断指定文件名是否已有处理记录（半行不计入）。</summary>
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

            // 与 WorkerReloadConfigRegressionTests 同一规则：只认写完整的 JSON 行，半行不计入；
            // 记录键是绝对路径明文，按文件名子串匹配绕开 Windows 路径的 JSON 转义。
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

    /// <summary>best-effort 清理临时夹具：FSW 句柄释放有延迟，删除失败不掩盖断言结果。</summary>
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
