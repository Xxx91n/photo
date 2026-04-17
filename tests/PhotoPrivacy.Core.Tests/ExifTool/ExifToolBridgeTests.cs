using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolBridgeTests
{
    [Fact]
    public async Task WipeMetadataAsync_Should_Complete_When_TaskDone_Line_Arrives()
    {
        var process = new FakeExifToolProcess();
        var bridge = new ExifToolBridge(process, AppConfig.Default);
        await bridge.StartAsync(CancellationToken.None);

        await bridge.WipeMetadataAsync(@"D:\hot\a.jpg", CancellationToken.None);

        Assert.True(process.Writes.Count >= 1);
        Assert.Contains(process.Writes, w => w.Contains("-echo1 TASK_DONE_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WipeMetadataAsync_Should_Timeout_And_Remove_Pending_Task_When_Marker_Missing()
    {
        var process = new FakeExifToolProcess
        {
            AutoEmitTaskDone = false
        };
        var bridge = new ExifToolBridge(process, AppConfig.Default);
        await bridge.StartAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => bridge.WipeMetadataAsync(@"D:\hot\timeout.jpg", cts.Token));

        Assert.Equal(0, bridge.PendingCount);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Start_Process_When_Not_Started()
    {
        var process = new FakeExifToolProcess();
        var bridge = new ExifToolBridge(process, AppConfig.Default);

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Equal(1, process.StartCalls);
        Assert.Equal(AppConfig.Default.ExifTool.Path, process.ExePath);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Restart_Process_When_HealthCheck_Times_Out()
    {
        var process = new FakeExifToolProcess
        {
            SuppressHealthReady = true
        };
        var bridge = new ExifToolBridge(process, AppConfig.Default);

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.True(process.StartCalls >= 2);
        Assert.True(process.StopCalls >= 1);
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Probe_ExifTool_Version_On_First_Start()
    {
        var process = new FakeExifToolProcess();
        var bridge = new ExifToolBridge(process, AppConfig.Default);

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(process.Writes, w => w.Contains("-ver", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Emit_ExifToolStarted_Lifecycle_Event_On_First_Start()
    {
        var process = new FakeExifToolProcess();
        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            AppConfig.Default,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            });

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(lifecycleEvents, ev => ev.EventType == "exiftool_started");
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Emit_ExifToolRestarted_Lifecycle_Event_When_HealthCheck_Times_Out()
    {
        var process = new FakeExifToolProcess
        {
            SuppressHealthReady = true
        };

        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            AppConfig.Default,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            },
            healthTimeout: TimeSpan.FromMilliseconds(100));

        await bridge.EnsureStartedAsync(CancellationToken.None);
        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(lifecycleEvents, ev => ev.EventType == "exiftool_restarted");
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Log_Warning_When_WindowsLongPath_Known_Issue_Version_Detected()
    {
        var process = new FakeExifToolProcess
        {
            VersionText = "13.05"
        };
        var logger = new ListLogger<ExifToolBridge>();
        var bridge = new ExifToolBridge(process, AppConfig.Default, logger, healthTimeout: TimeSpan.FromMilliseconds(200));

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(
            logger.Entries,
            x => x.Level == LogLevel.Warning
                && x.Message.Contains("WindowsLongPath", StringComparison.OrdinalIgnoreCase)
                && x.Message.Contains("known stay_open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EnsureStartedAsync_Should_Emit_VersionWarning_Lifecycle_Event_When_Known_Issue_Version_Detected()
    {
        var process = new FakeExifToolProcess
        {
            VersionText = "13.05"
        };

        var lifecycleEvents = new List<ExifToolLifecycleEvent>();
        var bridge = new ExifToolBridge(
            process,
            AppConfig.Default,
            logger: null,
            lifecycleSink: (ev, _) =>
            {
                lifecycleEvents.Add(ev);
                return ValueTask.CompletedTask;
            },
            healthTimeout: TimeSpan.FromMilliseconds(200));

        await bridge.EnsureStartedAsync(CancellationToken.None);

        Assert.Contains(
            lifecycleEvents,
            ev => ev.EventType == "exiftool_version_warning"
                && ev.Message.Contains("WindowsLongPath", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeExifToolProcess : IExifToolProcess
    {
        public event Action<string>? StdoutLine;

        public string ExePath { get; private set; } = string.Empty;
        public IReadOnlyList<string> Args => _args;
        public List<string> Writes { get; } = [];

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool SuppressHealthReady { get; set; }
        public bool AutoEmitTaskDone { get; set; } = true;
        public string VersionText { get; set; } = "13.20";

        private readonly List<string> _args = [];

        public Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
        {
            StartCalls++;
            ExePath = exePath;
            _args.Clear();
            _args.AddRange(args);
            return Task.CompletedTask;
        }

        public Task WriteStdinAsync(string text, CancellationToken cancellationToken)
        {
            Writes.Add(text);

            var markerLines = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains("-echo1", StringComparison.Ordinal))
                .Select(l => l.Replace("-echo1", string.Empty, StringComparison.Ordinal).Trim())
                .ToArray();

            foreach (var marker in markerLines)
            {
                if (marker.Contains("HEALTH_", StringComparison.Ordinal) && !SuppressHealthReady)
                {
                    EmitStdout(marker);
                }

                if (marker.Contains("TASK_DONE_", StringComparison.Ordinal) && AutoEmitTaskDone)
                {
                    EmitStdout(marker);
                }

                if (marker.Contains("VERSION_DONE_", StringComparison.Ordinal))
                {
                    EmitStdout(VersionText);
                    EmitStdout(marker);
                }
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public void EmitStdout(string line)
        {
            StdoutLine?.Invoke(line);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
