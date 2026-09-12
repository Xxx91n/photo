using System.Collections.Concurrent;
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.Watcher;

namespace PhotoPrivacy.Core.Tests.Watcher;

public sealed class FswFolderWatcherTests
{
    [Fact]
    public void Start_Should_Apply_64KB_InternalBufferSize_To_HotFolder_Watcher()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var factory = new RecordingFileSystemWatcherFactory();

        var cfg = AppConfig.Default with
        {
            Watch = AppConfig.Default.Watch with { HotFolder = tempRoot }
        };
        var watcher = new FswFolderWatcher(cfg, new FakeRecoveryScanner([]), new InMemoryAuditLogger(), _ => Task.CompletedTask, factory);

        try
        {
            // B03 检查点 A：默认值即 64KB（非分页池成本仅落在监控目录 watcher 上）
            Assert.Equal(65536, AppConfig.Default.Watch.InternalBufferSize);
            watcher.Start();
            var created = Assert.Single(factory.Created);
            Assert.Equal(65536, created.InternalBufferSize);
        }
        finally
        {
            watcher.Stop();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_Error_Events_Are_Coalesced_Into_Bounded_Recovery_Passes()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var lost1 = Path.Combine(tempRoot, "lost1.jpg");
        var lost2 = Path.Combine(tempRoot, "lost2.jpg");
        var cfg = AppConfig.Default with
        {
            Watch = AppConfig.Default.Watch with { HotFolder = tempRoot }
        };
        var scanner = new FakeRecoveryScanner([lost1, lost2]);
        var audit = new InMemoryAuditLogger();
        var collected = new ConcurrentBag<string>();
        var createEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new BlockingOnNthCreateFactory(blockIndex: 2, createEntered, releaseCreate);

        var watcher = new FswFolderWatcher(cfg, scanner, audit, path =>
        {
            collected.Add(path);
            return Task.CompletedTask;
        }, factory);

        try
        {
            watcher.Start();
            Assert.Equal(1, factory.CreatedCount);

            var recoveryPass = Task.Run(() => watcher.RecoverFromErrorAsync(new InternalBufferOverflowException("overflow")));
            await createEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2, factory.CreatedCount);

            var lateErrors = Enumerable.Range(0, 3)
                .Select(_ => Task.Run(() => watcher.RecoverFromErrorAsync(new InternalBufferOverflowException("overflow-while-recovering"))))
                .ToArray();
            await Task.WhenAll(lateErrors).WaitAsync(TimeSpan.FromSeconds(30));

            releaseCreate.TrySetResult();
            await recoveryPass.WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(3, factory.CreatedCount);
            Assert.Equal(2, factory.DisposedCount);
            Assert.Equal(2, audit.Events.Count(e => e.EventType == "fsw_recovered"));
            Assert.Contains(lost1, collected);
            Assert.Contains(lost2, collected);
        }
        finally
        {
            watcher.Stop();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HighFrequency_Write_Burst_Should_Not_Lose_Events()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        const int fileCount = 400;
        var cfg = AppConfig.Default with
        {
            Watch = AppConfig.Default.Watch with { HotFolder = tempRoot, PollingIntervalSeconds = 0 }
        };
        var audit = new InMemoryAuditLogger();
        var scanner = new DirectoryListingRecoveryScanner();
        var delivered = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var allDelivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new FswFolderWatcher(cfg, scanner, audit, path =>
        {
            if (delivered.TryAdd(path, 0) && delivered.Count >= fileCount)
            {
                allDelivered.TrySetResult();
            }
            return Task.CompletedTask;
        });

        try
        {
            watcher.Start();

            await Task.Run(() => Parallel.For(0, fileCount, i =>
            {
                File.WriteAllText(Path.Combine(tempRoot, $"burst-{i:D5}.txt"), $"payload-{i}");
            }));

            var completed = await Task.WhenAny(allDelivered.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            await Task.Delay(TimeSpan.FromSeconds(2));

            var overflowCount = audit.Events.Count(e => e.EventType == "fsw_error");
            Assert.True(
                completed == allDelivered.Task,
                $"only {delivered.Count}/{fileCount} delivered, fsw_error events: {overflowCount}");

            var missing = Enumerable.Range(0, fileCount)
                .Select(i => Path.Combine(tempRoot, $"burst-{i:D5}.txt"))
                .Where(p => !delivered.ContainsKey(p))
                .ToList();
            Assert.Empty(missing);
        }
        finally
        {
            watcher.Stop();
            Directory.Delete(tempRoot, recursive: true);
        }
    }

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

    private sealed class DirectoryListingRecoveryScanner : IRecoveryScanner
    {
        public IReadOnlyList<string> ScanAll(string rootPath)
        {
            return Directory.Exists(rootPath)
                ? Directory.EnumerateFiles(rootPath, "*.txt", SearchOption.AllDirectories).ToArray()
                : [];
        }
    }

    private sealed class RecordingFileSystemWatcherFactory : IFileSystemWatcherFactory
    {
        public List<FileSystemWatcher> Created { get; } = [];

        public FileSystemWatcher Create(string path)
        {
            var watcher = new FileSystemWatcher(path);
            Created.Add(watcher);
            return watcher;
        }
    }

    private sealed class BlockingOnNthCreateFactory(
        int blockIndex,
        TaskCompletionSource createEntered,
        TaskCompletionSource releaseCreate) : IFileSystemWatcherFactory
    {
        private int _created;

        public int CreatedCount => _created;
        public int DisposedCount;

        public FileSystemWatcher Create(string path)
        {
            var n = Interlocked.Increment(ref _created);
            var watcher = new TrackedFileSystemWatcher(path, () => Interlocked.Increment(ref DisposedCount));
            if (n == blockIndex)
            {
                createEntered.TrySetResult();
                releaseCreate.Task.Wait(TimeSpan.FromSeconds(30));
            }
            return watcher;
        }
    }

    private sealed class TrackedFileSystemWatcher(string path, Action onDisposed) : FileSystemWatcher(path)
    {
        private readonly Action _onDisposed = onDisposed;

        protected override void Dispose(bool disposing)
        {
            _onDisposed();
            base.Dispose(disposing);
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
