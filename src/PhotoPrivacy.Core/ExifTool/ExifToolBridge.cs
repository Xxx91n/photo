using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ExifToolBridge : IExifToolBridge
{
    private readonly IExifToolProcess _process;
    private readonly AppConfig _config;
    private readonly ILogger<ExifToolBridge> _logger;
    private readonly Func<ExifToolLifecycleEvent, CancellationToken, ValueTask>? _lifecycleSink;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private readonly object _lifecycleGate = new();
    private readonly TimeSpan _healthTimeout;
    private static readonly TimeSpan VersionProbeTimeout = TimeSpan.FromSeconds(2);

    private bool _started;
    private int _taskId;

    public ExifToolBridge(IExifToolProcess process, AppConfig config, TimeSpan? healthTimeout = null)
        : this(process, config, NullLogger<ExifToolBridge>.Instance, lifecycleSink: null, healthTimeout)
    {
    }

    public ExifToolBridge(
        IExifToolProcess process,
        AppConfig config,
        ILogger<ExifToolBridge>? logger,
        Func<ExifToolLifecycleEvent, CancellationToken, ValueTask>? lifecycleSink = null,
        TimeSpan? healthTimeout = null)
    {
        _process = process;
        _config = config;
        _logger = logger ?? NullLogger<ExifToolBridge>.Instance;
        _lifecycleSink = lifecycleSink;
        _healthTimeout = healthTimeout ?? TimeSpan.FromMilliseconds(200);
        _process.StdoutLine += OnStdoutLine;
    }

    public int PendingCount => _pending.Count;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            _started = false;
        }

        foreach (var marker in _pending.Keys)
        {
            if (_pending.TryRemove(marker, out var tcs))
            {
                tcs.TrySetCanceled(cancellationToken);
            }
        }

        await _process.StopAsync(cancellationToken);
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        var shouldStart = false;
        lock (_lifecycleGate)
        {
            shouldStart = !_started;
        }

        if (shouldStart)
        {
            await _process.StartAsync(
                _config.ExifTool.Path,
                ExifToolCommandBuilder.BuildStartArguments(_config),
                cancellationToken);

            lock (_lifecycleGate)
            {
                _started = true;
            }

            await EmitLifecycleEventAsync(
                new ExifToolLifecycleEvent(
                    EventType: "exiftool_started",
                    SourcePath: _config.ExifTool.Path,
                    Message: "ExifTool stay_open process started."),
                cancellationToken);

            await ProbeVersionAndWarnIfNeededAsync(cancellationToken);

            return;
        }

        var healthy = await TryHealthCheckAsync(cancellationToken);
        if (!healthy)
        {
            await RestartAsync(cancellationToken);
        }
    }

    public async Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        var id = Interlocked.Increment(ref _taskId).ToString();
        var marker = $"TASK_DONE_{id}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[marker] = tcs;

        try
        {
            var block = ExifToolCommandBuilder.BuildWipeTaskBlock(targetPath, id);
            await _process.WriteStdinAsync(block, cancellationToken);
            await tcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pending.TryRemove(marker, out _);
        }
    }

    private async Task<bool> TryHealthCheckAsync(CancellationToken cancellationToken)
    {
        var marker = $"HEALTH_{Interlocked.Increment(ref _taskId)}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[marker] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_healthTimeout);

        try
        {
            // Use explicit \n (not AppendLine/\r\n) — ExifTool stay_open protocol
            // requires LF-only line endings; \r\n breaks argument parsing.
            var cmd = $"-echo1 {marker}\n-execute\n";

            await _process.WriteStdinAsync(cmd, cancellationToken);
            await tcs.Task.WaitAsync(timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            _pending.TryRemove(marker, out _);
        }
    }

    private async Task RestartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _process.StopAsync(cancellationToken);
        }
        catch
        {
            // swallow restart-stop errors, continue trying to re-create process
        }

        await _process.StartAsync(
            _config.ExifTool.Path,
            ExifToolCommandBuilder.BuildStartArguments(_config),
            cancellationToken);

        lock (_lifecycleGate)
        {
            _started = true;
        }

        await EmitLifecycleEventAsync(
            new ExifToolLifecycleEvent(
                EventType: "exiftool_restarted",
                SourcePath: _config.ExifTool.Path,
                Message: "ExifTool process restarted after failed health check."),
            cancellationToken);
    }

    private async Task ProbeVersionAndWarnIfNeededAsync(CancellationToken cancellationToken)
    {
        var marker = $"VERSION_DONE_{Interlocked.Increment(ref _taskId)}";
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? capturedVersionLine = null;

        void Handler(string line)
        {
            if (line.Contains(marker, StringComparison.Ordinal))
            {
                tcs.TrySetResult(capturedVersionLine);
                return;
            }

            if (line.StartsWith("HEALTH_", StringComparison.Ordinal)
                || line.StartsWith("TASK_DONE_", StringComparison.Ordinal)
                || line.StartsWith("VERSION_DONE_", StringComparison.Ordinal))
            {
                return;
            }

            if (capturedVersionLine is null && !string.IsNullOrWhiteSpace(line))
            {
                capturedVersionLine = line.Trim();
            }
        }

        _process.StdoutLine += Handler;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(VersionProbeTimeout);

            var cmd = $"-ver\n-echo1 {marker}\n-execute\n";
            await _process.WriteStdinAsync(cmd, cancellationToken);

            var versionRaw = await tcs.Task.WaitAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(versionRaw))
            {
                _logger.LogWarning("ExifTool version probe returned no version text.");
                return;
            }

            _logger.LogInformation("Detected ExifTool version: {VersionText}", versionRaw);

            var parsed = TryParseExifToolVersion(versionRaw);
            if (parsed is null)
            {
                _logger.LogWarning("Unable to parse ExifTool version from: {VersionText}", versionRaw);
                return;
            }

            if (_config.ExifTool.EnableWindowsLongPath && IsKnownWindowsLongPathIssueVersion(parsed))
            {
                var warningMessage =
                    $"ExifTool {parsed.ToString(2)} is in a known stay_open + WindowsLongPath issue range; consider upgrading ExifTool or disabling WindowsLongPath.";

                _logger.LogWarning(
                    "{WarningMessage}",
                    warningMessage);

                await EmitLifecycleEventAsync(
                    new ExifToolLifecycleEvent(
                        EventType: "exiftool_version_warning",
                        SourcePath: _config.ExifTool.Path,
                        Message: warningMessage,
                        Data: new Dictionary<string, string>
                        {
                            ["version"] = parsed.ToString(2)
                        }),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("ExifTool version probe timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExifTool version probe failed.");
        }
        finally
        {
            _process.StdoutLine -= Handler;
        }
    }

    private static Version? TryParseExifToolVersion(string versionText)
    {
        var match = Regex.Match(versionText, @"\d+(?:\.\d+)+");
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Value, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsKnownWindowsLongPathIssueVersion(Version version)
    {
        var minInclusive = new Version(13, 5);
        var maxExclusive = new Version(13, 6);
        return version >= minInclusive && version < maxExclusive;
    }

    private async ValueTask EmitLifecycleEventAsync(ExifToolLifecycleEvent lifecycleEvent, CancellationToken cancellationToken)
    {
        if (_lifecycleSink is null)
        {
            return;
        }

        try
        {
            await _lifecycleSink(lifecycleEvent, cancellationToken);
        }
        catch
        {
            // lifecycle auditing must never break bridge flow
        }
    }

    private void OnStdoutLine(string line)
    {
        foreach (var marker in _pending.Keys)
        {
            if (!line.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (_pending.TryRemove(marker, out var tcs))
            {
                tcs.TrySetResult(true);
            }
        }
    }
}
