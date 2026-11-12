using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Pipeline;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Pipeline;

public sealed class FileTaskPipelineTests
{
    [Fact]
    public async Task HandleAsync_Should_Move_To_Quarantine_After_Max_Retries()
    {
        // 返修：隔离子、拼接断言平台中立（原 D:\ 字面量在 Linux 与 Path.Combine 结果不匹配）
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 2, BackoffSeconds = [0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "a.jpg"), CancellationToken.None);

        Assert.Contains(fileOps.Moves, m => m.Source == Path.Combine(hot, "a.jpg") && m.Destination == Path.Combine(quarantine, "a.jpg"));
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_And_Quarantine_Without_Retry_When_Bridge_Reports_Write_Error()
    {
        // #288：ExifTool 明确写不了（分片 MP4 即此形态）时，审计必须是 file_processing_failed，
        // 绝不许 file_processing_succeeded；确定性错误重试必然同样失败，不进重试循环，
        // 直接按设置进隔离区，且不写 processed 记录（下轮扫描不再走已处理挡板）。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 3, BackoffSeconds = [0, 0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "fragmented.mp4"), CancellationToken.None);

        Assert.Equal(1, bridge.Calls);
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_retry_scheduled");
        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_processing_failed"
                && e.Message.Contains("wipe_result=Failed_WriteError", StringComparison.Ordinal));
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
        Assert.Contains(
            fileOps.Moves,
            m => m.Source == Path.Combine(hot, "fragmented.mp4")
                && m.Destination == Path.Combine(quarantine, "fragmented.mp4"));
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_Without_Quarantine_When_Quarantine_Disabled()
    {
        // #288：隔离未启用时不挪文件（也无隔离事件），但失败审计照写——用户至少看得到失败。
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Enabled = false }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(@"D:\hot\a.mp4", CancellationToken.None);

        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_processing_failed"
                && e.Message.Contains("wipe_result=Failed_WriteError", StringComparison.Ordinal));
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_quarantined");
        Assert.Empty(fileOps.Moves);
    }

    [Fact]
    public async Task HandleAsync_Should_Skip_When_File_Is_InFlight()
    {
        var cfg = AppConfig.Default;
        var ruleEngine = new RuleEngine(cfg);
        var bridge = new SlowSuccessBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        var t1 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);
        var t2 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        await Task.WhenAll(t1, t2);
        Assert.Equal(1, bridge.Calls);
    }

    [Fact]
    public async Task HandleAsync_Should_Copy_To_Fixed_Output_Before_Wiping()
    {
        // 返修：fixed_directory 拼接 = Combine(outputDirectory, relative)——平台中立构造
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var clean = Path.Combine(Path.GetTempPath(), "pp-ft-clean");
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = clean
            },
            Watch = AppConfig.Default.Watch with { HotFolder = hot },
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new CaptureTargetBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "album", "a.jpg"), CancellationToken.None);

        Assert.Contains(fileOps.Copies, c => c.Source == Path.Combine(hot, "album", "a.jpg") && c.Destination == Path.Combine(clean, "album", "a.jpg"));
        Assert.Equal(Path.Combine(clean, "album", "a.jpg"), bridge.LastTargetPath);
    }

    [Fact]
    public async Task HandleAsync_Should_Write_FileDetected_And_FileProcessingStarted_Before_Success()
    {
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new CaptureTargetBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        var eventTypes = audit.Events.Select(x => x.EventType).ToList();
        var detectedIndex = eventTypes.IndexOf("file_detected");
        var startedIndex = eventTypes.IndexOf("file_processing_started");
        var succeededIndex = eventTypes.IndexOf("file_processing_succeeded");

        Assert.True(detectedIndex >= 0, "missing file_detected");
        Assert.True(startedIndex >= 0, "missing file_processing_started");
        Assert.True(succeededIndex >= 0, "missing file_processing_succeeded");
        Assert.True(detectedIndex < startedIndex, "file_detected should appear before file_processing_started");
        Assert.True(startedIndex < succeededIndex, "file_processing_started should appear before file_processing_succeeded");
    }

    [Fact]
    public async Task HandleAsync_Should_Move_Output_File_To_Quarantine_When_Retry_Exhausted_And_Output_Is_Fixed()
    {
        // 返修：隔离 quarantine 拼接 = Combine(directory, fileName)——平台中立构造
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var clean = Path.Combine(Path.GetTempPath(), "pp-ft-clean");
        var quarantine = Path.Combine(Path.GetTempPath(), "pp-ft-quar");
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = clean
            },
            Watch = AppConfig.Default.Watch with { HotFolder = hot },
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Enabled = true, Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "album", "a.jpg"), CancellationToken.None);

        Assert.Contains(fileOps.Moves, m => m.Source == Path.Combine(clean, "album", "a.jpg") && m.Destination == Path.Combine(quarantine, "a.jpg"));
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
    }

    [Fact]
    public async Task HandleAsync_Should_Write_FileRetryScheduled_Before_Second_Attempt()
    {
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 2, BackoffSeconds = [0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = @"D:\hot\_quarantine" }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_retry_scheduled"
                && e.Message.Contains("next_attempt=2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_Should_Skip_Rescan_Of_Same_Path_After_Content_Changed()
    {
        // 要防的老毛病：same_as_source 清洗后文件长度/mtime 指纹变化，旧去重键（路径|长度|mtime）
        // 不再命中，整目录补漏扫把已清洗文件又处理一遍（#133）。
        // 关联 issue #133。
        // 路线 2 规则：去重键收敛为规范化路径粗粒度命中——同一路径已有记录即视为已处理，不再比对指纹。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-rescan-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hot);
        var file = Path.Combine(hot, "a.jpg");
        File.WriteAllBytes(file, [1, 2, 3]);

        try
        {
            var cfg = AppConfig.Default with
            {
                Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
                Backup = AppConfig.Default.Backup with { Enabled = false }
            };
            var ruleEngine = new RuleEngine(cfg);
            var bridge = new SlowSuccessBridge();
            var fileOps = new InMemoryFileOperations();
            var audit = new InMemoryAuditLogger();
            var store = new InMemoryProcessedRecordStore();
            var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, store);

            await pipeline.HandleAsync(file, CancellationToken.None);
            Assert.Equal(1, bridge.Calls);

            // 模拟清洗后文件内容/指纹变化（same_as_source 会改长度和 mtime）：补漏扫同一路径应被路径命中挡住。
            File.WriteAllBytes(file, [9, 9, 9, 9, 9]);

            await pipeline.HandleAsync(file, CancellationToken.None);

            Assert.Equal(1, bridge.Calls);
            Assert.Contains(audit.Events, e => e.EventType == "file_skipped" && e.Message == "already_processed");
        }
        finally
        {
            Directory.Delete(hot, recursive: true);
        }
    }

    private sealed class InMemoryFileOperations : IFileOperations
    {
        public List<(string Source, string Destination)> Moves { get; } = [];
        public List<(string Source, string Destination, bool Overwrite)> Copies { get; } = [];
        public List<string> EnsuredDirectories { get; } = [];

        public void EnsureDirectory(string path)
        {
            EnsuredDirectories.Add(path);
        }

        public void Move(string source, string destination)
        {
            Moves.Add((source, destination));
        }

        public void Copy(string source, string destination, bool overwrite)
        {
            Copies.Add((source, destination, overwrite));
        }

        public void AtomicCopy(string source, string destination, bool overwrite)
        {
            Copies.Add((source, destination, overwrite));
        }

        public Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
        {
            Copies.Add((source, destination, overwrite));
            return Task.CompletedTask;
        }

        public Task AtomicCopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
        {
            Copies.Add((source, destination, overwrite));
            return Task.CompletedTask;
        }

        public Task MoveAsync(string source, string destination, CancellationToken cancellationToken)
        {
            Moves.Add((source, destination));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuditLogger : IAuditLogger
    {
        public List<AuditEvent> Events { get; } = [];

        public ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysFailBridge : IExifToolBridge
    {
        public string VersionText => "test";

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("fail");
        }
    }

    private sealed class WriteErrorBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public int Calls { get; private set; }

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(WipeResult.Failed_WriteError);
        }
    }

    private sealed class CaptureTargetBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public string LastTargetPath { get; private set; } = string.Empty;

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            LastTargetPath = targetPath;
            return Task.FromResult(WipeResult.Cleaned_Modified);
        }
    }

    private sealed class SlowSuccessBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public int Calls { get; private set; }

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(50, cancellationToken);
            return WipeResult.Cleaned_Modified;
        }
    }
}
