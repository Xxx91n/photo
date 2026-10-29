using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;

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
/// 本用例直接驱动真实 worker（dry-run 桥，不 spawn ExifTool），用「重载后热文件夹仍被监视、
/// 新文件仍被处理」区分新旧实现：旧形态重载在新建 store 时就抛（被吞掉），旧 watcher 已停、
/// 新 watcher 从未创建——文件不再被处理，必红。
/// </summary>
public sealed class WorkerReloadConfigRegressionTests
{
    [Fact]
    public async Task ReloadConfig_Should_Keep_Watching_And_Processing_After_Repeated_Reloads()
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

        try
        {
            await worker.StartAsync(CancellationToken.None);

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

            // 连续两次重载：旧 store 的常驻写句柄必须先释放，否则新 store 构造即冲突。
            await worker.ReloadConfigAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await worker.ReloadConfigAsync().WaitAsync(TimeSpan.FromSeconds(30));

            // 重载真正生效的判据：watcher/pipeline 已重建并在跑，新文件继续被处理。
            // 旧形态在此必红——重载抛在 store 构造处（被吞），watcher 停掉后再没重建。
            await File.WriteAllTextAsync(Path.Combine(hot, "after-reload.jpg"), "after-reload");
            Assert.True(
                await WaitForProcessedRecordsAsync(processedPath, expectedAtLeast: 2, TimeSpan.FromSeconds(30)),
                "重载后热文件夹不再处理文件：ReloadConfig 没有真正生效（#85）");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            TryDelete(root);
        }
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
    /// 等处理记录条数达到预期。<c>processed.ndjson</c> 以 <c>FileShare.Read</c> 常驻写句柄打开，
    /// 读侧可并发读取；每成功处理一个文件追加一行。
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
        try
        {
            return File.Exists(processedPath)
                ? File.ReadAllLines(processedPath).Count(line => !string.IsNullOrWhiteSpace(line))
                : 0;
        }
        catch (IOException)
        {
            // 写入瞬间的竞争：下一轮再读。
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
