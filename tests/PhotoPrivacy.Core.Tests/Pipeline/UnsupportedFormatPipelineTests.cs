using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Pipeline;
using PhotoPrivacy.Core.Rules;
using PhotoPrivacy.Core.Tests.ExifTool;

namespace PhotoPrivacy.Core.Tests.Pipeline;

/// <summary>
/// 票 01（A-001 / 不变量①fail-closed ④驻守可观测）：挂死链行为测试——喂未映射格式走完管道。
///
/// 场景：升级前的旧配置（或用户自定义配置）把 .webp 这类“宣称支持但无擦除策略”的扩展名留在
/// allowed_extensions 里——管道会把它喂到 ExifToolBridge。原实现在这里永久挂起：
/// SKIP_ no-op 块永不匹配 TASK_DONE_ marker，TCS 挂到取消（“扔进一张 .webp 就卡住”）。
///
/// 本测试用真实 ExifToolBridge + 协议级 fake（不是回放式 fake）走完整管道，断言：
/// 1) 有限时间内完成（不挂死）；
/// 2) 产出可见的 wipe_skipped_unknown 审计事件；
/// 3) 不进 ExifTool（进程零启动、零写盘）——ADR 0053 M6f “不进 ExifTool”的逐字兼现。
/// </summary>
public sealed class UnsupportedFormatPipelineTests : IDisposable
{
    private readonly string _hotFolder;

    public UnsupportedFormatPipelineTests()
    {
        _hotFolder = Path.Combine(Path.GetTempPath(), "pp-unsupported-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_hotFolder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_hotFolder, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task HandleAsync_Should_Skip_Unmapped_Extension_With_Visible_Audit_Without_Hanging()
    {
        var source = Path.Combine(_hotFolder, "legacy.webp");
        File.WriteAllBytes(source, [0x52, 0x49, 0x46, 0x46]);

        var process = new ProtocolLevelFakeExifToolProcess();
        var audit = new InMemoryAuditLogger();
        var pipeline = BuildPipeline(process, audit, ".webp");

        await pipeline.HandleAsync(source, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        var skipEvent = audit.Events.FirstOrDefault(e => e.EventType == "wipe_skipped_unknown");
        Assert.NotNull(skipEvent);
        Assert.Equal(AuditLevel.Warn, skipEvent!.Level);
        Assert.Equal(".webp", skipEvent.Data?["extension"]);
        Assert.Equal(0, process.StartCalls);
        Assert.Empty(process.Writes);
    }

    [Fact]
    public async Task HandleAsync_Should_Still_Process_Mapped_Extension_After_Claim_Convergence()
    {
        var source = Path.Combine(_hotFolder, "keep.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xFF]);

        var process = new ProtocolLevelFakeExifToolProcess();
        var audit = new InMemoryAuditLogger();
        var pipeline = BuildPipeline(process, audit, ".jpg");

        await pipeline.HandleAsync(source, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "wipe_skipped_unknown");
        Assert.True(process.StartCalls >= 1);
    }

    private FileTaskPipeline BuildPipeline(
        ProtocolLevelFakeExifToolProcess process,
        InMemoryAuditLogger audit,
        string allowedExtension)
    {
        var exifToolPath = OperatingSystem.IsWindows()
            ? @"C:\Program Files\ExifTool\exiftool.exe"
            : "/usr/bin/exiftool";

        var config = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with { Path = exifToolPath },
            Rules = AppConfig.Default.Rules with { AllowedExtensions = [allowedExtension] },
            Watch = AppConfig.Default.Watch with { HotFolder = _hotFolder },
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] }
        };

        var bridge = new ExifToolBridge(process, config);

        return new FileTaskPipeline(
            config,
            new RuleEngine(config),
            bridge,
            new InMemoryFileOperations(),
            audit,
            new InMemoryProcessedRecordStore());
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

    private sealed class InMemoryFileOperations : IFileOperations
    {
        public List<(string Source, string Destination)> Moves { get; } = [];

        public List<(string Source, string Destination, bool Overwrite)> Copies { get; } = [];

        public void EnsureDirectory(string path)
        {
        }

        public void Move(string source, string destination) => Moves.Add((source, destination));

        public void Copy(string source, string destination, bool overwrite) => Copies.Add((source, destination, overwrite));

        public void AtomicCopy(string source, string destination, bool overwrite) => Copies.Add((source, destination, overwrite));

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
}
