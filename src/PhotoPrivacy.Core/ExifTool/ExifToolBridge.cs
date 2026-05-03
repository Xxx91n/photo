using System.Collections.Concurrent;
using System.Text.Json;
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
    private readonly TimeSpan _startupTimeout;
    private static readonly TimeSpan RunningHealthTimeout = TimeSpan.FromMilliseconds(500);

    private bool _started;
    private int _taskId;
    private string _versionText = "unknown";

    public string VersionText => _versionText;

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
        _startupTimeout = healthTimeout ?? TimeSpan.FromSeconds(3);
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
            var startArgs = ExifToolCommandBuilder.BuildStartArguments(_config);

            await _process.StartAsync(
                _config.ExifTool.Path,
                startArgs,
                cancellationToken);

            lock (_lifecycleGate)
            {
                _started = true;
            }

            await EmitLifecycleEventAsync(
                new ExifToolLifecycleEvent(
                    EventType: "exiftool_started",
                    SourcePath: _config.ExifTool.Path,
                    Message: "ExifTool stay_open process started.",
                    Data: new Dictionary<string, string>
                    {
                        ["exe_path"] = _config.ExifTool.Path,
                        ["arguments"] = string.Join(" ", startArgs)
                    }),
                cancellationToken);

            await ProbeVersionAndWarnIfNeededAsync(cancellationToken);

            return;
        }

        var health = await TryHealthCheckAsync(cancellationToken, RunningHealthTimeout);
        if (!health.IsHealthy)
        {
            await RestartAsync(cancellationToken, health);
        }
    }

    public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        var id = Interlocked.Increment(ref _taskId).ToString();

        // Phase 1: Probe metadata via -json
        var probeMarker = $"PROBE_DONE_{id}";
        var probeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        string? probeOutput = null;
        var probeLines = new List<string>();

        void ProbeLineHandler(string line)
        {
            if (line.Contains(probeMarker, StringComparison.Ordinal))
            {
                probeOutput = string.Join(Environment.NewLine, probeLines).Trim();
                probeTcs.TrySetResult(probeOutput);
                return;
            }
            probeLines.Add(line);
        }

        _process.StdoutLine += ProbeLineHandler;
        try
        {
            var probeBlock = ExifToolCommandBuilder.BuildProbeTaskBlock(targetPath, id);
            await _process.WriteStdinAsync(probeBlock, cancellationToken);
            probeOutput = await probeTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _process.StdoutLine -= ProbeLineHandler;
        }

        // Phase 2: Check probe result
        var hasClearableMetadata = !string.IsNullOrWhiteSpace(probeOutput) && HasClearableFields(probeOutput, targetPath);
        if (!hasClearableMetadata)
        {
            return string.IsNullOrWhiteSpace(probeOutput)
                ? WipeResult.Skipped_NoMetadata
                : WipeResult.Skipped_NoClearable;
        }

        // Phase 3: Wipe
        var wipeMarker = $"TASK_DONE_{id}";
        var wipeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[wipeMarker] = wipeTcs;

        FileInfo beforeInfo;
        long beforeLength;
        DateTime beforeWriteTime;
        try
        {
            beforeInfo = new FileInfo(targetPath);
            beforeLength = beforeInfo.Length;
            beforeWriteTime = beforeInfo.LastWriteTimeUtc;
        }
        catch
        {
            beforeLength = -1;
            beforeWriteTime = DateTime.MinValue;
        }

        try
        {
            var wipeBlock = ExifToolCommandBuilder.BuildWipeTaskBlock(targetPath, id);
            await _process.WriteStdinAsync(wipeBlock, cancellationToken);
            await wipeTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pending.TryRemove(wipeMarker, out _);
        }

        try
        {
            var afterInfo = new FileInfo(targetPath);
            afterInfo.Refresh();
            if (afterInfo.Exists)
            {
                var changed = afterInfo.Length != beforeLength
                              || afterInfo.LastWriteTimeUtc != beforeWriteTime;
                return changed ? WipeResult.Cleaned_Modified : WipeResult.Cleaned_NoOp;
            }
        }
        catch
        {
        }

        return WipeResult.Cleaned_Modified;
    }

    private static readonly HashSet<string> _nonPrivacyFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "SourceFile", "Directory", "FileName", "FileSize", "FileModifyDate",
        "FileAccessDate", "FileCreateDate", "FileInodeChangeDate", "FilePermissions",
        "FileType", "FileTypeExtension", "MIMEType", "Compression", "Filter",
        "Interlace", "ColorType", "BitDepth", "ImageWidth", "ImageHeight",
        "ImageSize", "Megapixels", "XResolution", "YResolution", "ResolutionUnit",
        "Error", "ExifToolVersion", "Warning",
        "EncodingProcess", "YCbCrSubSampling", "ColorComponents",
        "Quality", "NumImportantColors", "DCTEncodeVersion",
    };

    private static bool HasClearableFields(string jsonOutput, string targetPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return false;

            var entry = root[0];
            foreach (var prop in entry.EnumerateObject())
            {
                if (!_nonPrivacyFields.Contains(prop.Name))
                    return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<HealthCheckResult> TryHealthCheckAsync(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var marker = $"HEALTH_{Interlocked.Increment(ref _taskId)}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[marker] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            // Use explicit \n (not AppendLine/\r\n) — ExifTool stay_open protocol
            // requires LF-only line endings; \r\n breaks argument parsing.
            var cmd = $"-fast\n-echo1\n{marker}\n{_config.ExifTool.Path}\n-execute\n";

            await _process.WriteStdinAsync(cmd, cancellationToken);
            await tcs.Task.WaitAsync(timeoutCts.Token);
            return HealthCheckResult.Success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var data = BuildHealthFailureData(
                reason: "timeout",
                detail: $"health check timed out after {timeout.TotalMilliseconds:F0} ms");

            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "timeout",
                Message: "health check timeout",
                Data: data);
        }
        catch (Exception ex)
        {
            var data = BuildHealthFailureData(
                reason: "exception",
                detail: ex.Message);

            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "exception",
                Message: ex.Message,
                Data: data);
        }
        finally
        {
            _pending.TryRemove(marker, out _);
        }
    }

    private async Task RestartAsync(CancellationToken cancellationToken, HealthCheckResult healthResult)
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
                Message: $"ExifTool process restarted after failed health check: {healthResult.Reason} ({healthResult.Message})",
                Data: healthResult.Data),
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
            timeoutCts.CancelAfter(_startupTimeout);

            var cmd = $"-ver\n-echo1\n{marker}\n-execute\n";
            await _process.WriteStdinAsync(cmd, cancellationToken);

            var versionRaw = await tcs.Task.WaitAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(versionRaw))
            {
                _logger.LogWarning("ExifTool version probe returned no version text.");
                return;
            }

            _logger.LogInformation("Detected ExifTool version: {VersionText}", versionRaw);
            _versionText = versionRaw;

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

    private Dictionary<string, string> BuildHealthFailureData(string reason, string detail)
    {
        var data = new Dictionary<string, string>
        {
            ["reason"] = reason,
            ["detail"] = detail,
            ["process_running"] = _process.IsRunning.ToString()
        };

        if (!string.IsNullOrWhiteSpace(_process.LastStderrLine))
        {
            data["stderr_last_line"] = _process.LastStderrLine!;
        }

        return data;
    }

    private sealed record HealthCheckResult(
        bool IsHealthy,
        string Reason,
        string Message,
        Dictionary<string, string>? Data)
    {
        public static readonly HealthCheckResult Success = new(
            IsHealthy: true,
            Reason: string.Empty,
            Message: string.Empty,
            Data: null);
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
