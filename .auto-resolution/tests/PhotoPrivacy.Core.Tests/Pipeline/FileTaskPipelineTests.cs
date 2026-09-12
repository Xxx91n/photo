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
        // 票 28 返修：隔离子、拼接断言平台中立（原 D:\ 字面量在 Linux 与 Path.Combine 结果不匹配）
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
        // 票 28 返修：fixed_directory 拼接 = Combine(outputDirectory, relative)——平台中立构造
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
        // 票 28 返修：隔离 quarantine 拼接 = Combine(directory, fileName)——平台中立构造
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

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("fail");
        }
    }

    private sealed class CaptureTargetBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public string LastTargetPath { get; private set; } = string.Empty;

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
