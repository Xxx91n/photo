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
        var factory = new FakeFileSystemWatcherFactory();

        var watcher = new FswFolderWatcher(cfg, scanner, audit, path =>
        {
            collected.Add(path);
            return Task.CompletedTask;
        }, factory);

        try
        {
            watcher.Start();
            await watcher.RecoverFromErrorAsync(new InternalBufferOverflowException("overflow"));

            Assert.Contains(lost1, collected);
            Assert.Contains(lost2, collected);
            Assert.Contains(audit.Events, e => e.EventType == "fsw_recovered");
            Assert.True(factory.CreatedCount >= 2);
        }
        finally
        {
            watcher.Stop();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class FakeFileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public int CreatedCount { get; private set; }

        public FileSystemWatcher Create(string path)
        {
            CreatedCount++;
            return new FileSystemWatcher(path);
        }
    }

    [Fact]
    public async Task Recover_Should_Skip_Audit_And_Quarantine_Subdirectories_Under_HotFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var audit = Path.Combine(tempRoot, "_audit");
        var quarantine = Path.Combine(tempRoot, "_quarantine");
        Directory.CreateDirectory(audit);
        Directory.CreateDirectory(quarantine);

        var keep = Path.Combine(tempRoot, "keep.jpg");
        var fromAudit = Path.Combine(audit, "audit-2026-04-18.jsonl");
        var fromQuarantine = Path.Combine(quarantine, "bad.jpg");

        var cfg = AppConfig.Default with
        {
            Watch = AppConfig.Default.Watch with { HotFolder = tempRoot },
            Audit = AppConfig.Default.Audit with { LogDirectory = audit },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var scanner = new FakeRecoveryScanner([keep, fromAudit, fromQuarantine]);
        var auditLogger = new InMemoryAuditLogger();
        var collected = new List<string>();
        var watcher = new FswFolderWatcher(cfg, scanner, auditLogger, path =>
        {
            collected.Add(path);
            return Task.CompletedTask;
        });

        try
        {
            await watcher.RecoverFromErrorAsync(new IOException("boom"));

            Assert.Contains(keep, collected);
            Assert.DoesNotContain(fromAudit, collected);
            Assert.DoesNotContain(fromQuarantine, collected);
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
