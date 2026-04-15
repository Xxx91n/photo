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
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 2, BackoffSeconds = [0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = @"D:\hot\_quarantine" }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit);

        await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        Assert.Contains(fileOps.Moves, m => m.Source == @"D:\hot\a.jpg" && m.Destination == @"D:\hot\_quarantine\a.jpg");
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
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit);

        var t1 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);
        var t2 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        await Task.WhenAll(t1, t2);
        Assert.Equal(1, bridge.Calls);
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
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("fail");
        }
    }

    private sealed class SlowSuccessBridge : IExifToolBridge
    {
        public int Calls { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(50, cancellationToken);
        }
    }
}
