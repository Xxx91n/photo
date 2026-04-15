using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Watcher;

namespace PhotoPrivacy.Core.Tests.Watcher;

public sealed class FswFolderWatcherTests
{
    [Fact]
    public async Task Error_Should_Rebuild_And_Run_Full_Recovery_Scan()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var cfg = AppConfig.Default with
        {
            Watch = AppConfig.Default.Watch with { HotFolder = tempRoot }
        };
        var lost1 = Path.Combine(tempRoot, "lost1.jpg");
        var lost2 = Path.Combine(tempRoot, "lost2.jpg");
        var scanner = new FakeRecoveryScanner([lost1, lost2]);
        var audit = new InMemoryAuditLogger();
        var collected = new List<string>();

        var watcher = new FswFolderWatcher(cfg, scanner, audit, path =>
        {
            collected.Add(path);
            return Task.CompletedTask;
        });

        try
        {
            await watcher.RecoverFromErrorAsync(new InternalBufferOverflowException("overflow"));

            Assert.Contains(lost1, collected);
            Assert.Contains(lost2, collected);
            Assert.Contains(audit.Events, e => e.EventType == "fsw_recovered");
        }
        finally
        {
            watcher.Stop();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class FakeRecoveryScanner(IReadOnlyList<string> paths) : IRecoveryScanner
    {
        public IReadOnlyList<string> ScanAll(string rootPath)
        {
            return paths;
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
}
