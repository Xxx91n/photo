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
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly TimeSpan _startupTimeout;
    private static readonly TimeSpan RunningHealthTimeout = TimeSpan.FromMilliseconds(500);

    private volatile bool _started;
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
        _started = false;

        foreach (var marker in _pending.Keys)
        {
            if (_pending.TryRemove(marker, out var tcs))
            {
                tcs.TrySetCanceled(cancellationToken);
            }
        }

        await _process.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 确保 ExifTool 进程已启动。使用 SemaphoreSlim 防止并发启动竞争。
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            var health = await TryHealthCheckAsync(cancellationToken, RunningHealthTimeout);
            if (!health.IsHealthy)
            {
                await RestartAsync(cancellationToken, health);
            }
            return;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：获取锁后再次检查，避免重复启动。
            if (_started)
            {
                return;
            }

            // 验证 ExifTool 路径在使用点仍然有效（防止热重载引入恶意路径）。
            ValidateExifToolPath(_config.ExifTool.Path);

            var startArgs = ExifToolCommandBuilder.BuildStartArguments(_config);

            await _process.StartAsync(
                _config.ExifTool.Path,
                startArgs,
                cancellationToken);

            _started = true;

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
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        var id = Interlocked.Increment(ref _taskId).ToString();

        // Phase 1: Probe metadata via -json
        var probeMarker = $"PROBE_DONE_{id}";
        var probeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var probeOutputBuilder = new System.Text.StringBuilder();
        var probeOutputLock = new object();

        // ExifTool stay_open protocol: -echo1 markers are echoed BEFORE file
        // processing output. So the order is:
        //   1. PROBE_DONE_{id}  (echo marker)
        //   2. [{...JSON...}]   (file metadata)
        //   3. {ready}           (task complete)
        // We use {ready} as the end-of-task delimiter.
        var markerSeen = false;
        void ProbeLineHandler(string line)
        {
            if (probeTcs.Task.IsCompleted)
                return;

            // 跳过 marker 行本身，只收集 marker 之后的数据。
            if (!markerSeen)
            {
                if (line.Contains(probeMarker, StringComparison.Ordinal))
                {
                    markerSeen = true;
                }
                return;
            }

            // {ready} prompt is ExifTool's end-of-task signal.
            if (line.StartsWith("{ready", StringComparison.Ordinal))
            {
                var result = probeOutputBuilder.ToString();
                probeTcs.TrySetResult(result);
                return;
            }

            // Collect lines between the echo marker and {ready}.
            lock (probeOutputLock)
            {
                probeOutputBuilder.AppendLine(line);
            }
        }

        _process.StdoutLine += ProbeLineHandler;
        try
        {
            var probeBlock = ExifToolCommandBuilder.BuildProbeTaskBlock(targetPath, id);
            await _process.WriteStdinAsync(probeBlock, cancellationToken);
            await probeTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _process.StdoutLine -= ProbeLineHandler;
        }

        // Phase 2: Check probe result
        var probeOutputResult = probeTcs.Task.IsCompleted ? probeTcs.Task.Result : null;
        var hasClearableMetadata = !string.IsNullOrWhiteSpace(probeOutputResult) && HasClearableFields(probeOutputResult, targetPath);
        if (!hasClearableMetadata)
        {
            if (string.IsNullOrWhiteSpace(probeOutputResult))
            {
                _logger.LogDebug(
                    "Probe returned empty output for {Path}; treating as no metadata.",
                    targetPath);
                return WipeResult.Skipped_NoMetadata;
            }
            return WipeResult.Skipped_NoClearable;
        }

        // Phase 3: Wipe
        var wipeMarker = $"TASK_DONE_{id}";
        var wipeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[wipeMarker] = wipeTcs;

        FileInfo? beforeInfo = null;
        long beforeLength = -1;
        DateTime beforeWriteTime = DateTime.MinValue;
        try
        {
            beforeInfo = new FileInfo(targetPath);
            beforeLength = beforeInfo.Length;
            beforeWriteTime = beforeInfo.LastWriteTimeUtc;
        }
        catch
        {
            // best-effort: post-check will be skipped
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
            if (beforeInfo is not null)
            {
                beforeInfo.Refresh();
                if (beforeInfo.Exists
                    && beforeInfo.Length == beforeLength
                    && beforeInfo.LastWriteTimeUtc == beforeWriteTime)
                {
                    _logger.LogWarning("ExifTool wipe completed but file appears unchanged for {Path}.", targetPath);
                }
            }
        }
        catch
        {
            // best-effort post-check
        }

        return WipeResult.Cleaned_Modified;
    }

    /// <summary>
    /// 验证 ExifTool 路径的安全性。在进程启动和热重载时调用。
    /// </summary>
    private static void ValidateExifToolPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("ExifTool 路径不能为空");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"ExifTool 路径必须是绝对路径: {path}");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("ExifTool 可执行文件不存在", path);
        }
    }

    private bool HasClearableFields(string jsonOutput, string targetPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return false;
            }

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // 如果有超过 2 个字段（除了 SourceFile 和 File），说明有可清除的元数据。
            return first.EnumerateObject().Count() >= 2;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse probe output for {Path}", targetPath);
            return false;
        }
    }

    private async Task RestartAsync(CancellationToken cancellationToken, HealthCheckResult reason)
    {
        _logger.LogWarning("Restarting ExifTool process: {Reason} - {Message}", reason.Reason, reason.Message);

        await EmitLifecycleEventAsync(
            new ExifToolLifecycleEvent(
                EventType: "exiftool_restarted",
                SourcePath: _config.ExifTool.Path,
                Message: $"Restarting ExifTool: {reason.Reason} - {reason.Message}",
                Data: reason.Data),
            cancellationToken);

        await _process.StopAsync(cancellationToken);
        _started = false;

        var startArgs = ExifToolCommandBuilder.BuildStartArguments(_config);
        await _process.StartAsync(_config.ExifTool.Path, startArgs, cancellationToken);
        _started = true;

        await ProbeVersionAndWarnIfNeededAsync(cancellationToken);
    }

    private async Task<HealthCheckResult> TryHealthCheckAsync(CancellationToken cancellationToken, TimeSpan timeout)
    {
        if (!_process.IsRunning)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "process_exited",
                Message: "ExifTool process is not running.",
                Data: BuildHealthFailureData("process_exited", "ExifTool process is not running."));
        }

        var marker = $"HEALTH_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[marker] = tcs;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            // Use explicit \n (not AppendLine/\r\n) — ExifTool stay_open protocol
            // requires LF-only line endings; \r\n breaks argument parsing.
            var cmd = $"-fast\n-echo1\n{marker}\n{_config.ExifTool.Path}\n-execute\n";

            await _process.WriteStdinAsync(cmd, cancellationToken);
            await tcs.Task.WaitAsync(timeoutCts.Token);
            return HealthCheckResult.Success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "health_timeout",
                Message: $"Health check timed out after {timeout.TotalMilliseconds}ms.",
                Data: BuildHealthFailureData("health_timeout", $"Timed out after {timeout.TotalMilliseconds}ms"));
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "health_error",
                Message: ex.Message,
                Data: BuildHealthFailureData("health_error", ex.Message));
        }
        finally
        {
            _pending.TryRemove(marker, out _);
        }
    }

    private async Task ProbeVersionAndWarnIfNeededAsync(CancellationToken cancellationToken)
    {
        var marker = $"VERSION_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        string? capturedVersionLine = null;

        var markerSeen = false;
        void Handler(string line)
        {
            if (tcs.Task.IsCompleted) return;

            // 跳过 {ready} 和 VERSION_DONE_ 信号
            if (line.StartsWith("{ready", StringComparison.Ordinal)
                || line.StartsWith("VERSION_DONE_", StringComparison.Ordinal))
            {
                return;
            }

            if (line.Contains(marker, StringComparison.Ordinal))
            {
                markerSeen = true;
                // 如果 version text 已经到达，立即返回结果
                if (capturedVersionLine is not null)
                {
                    tcs.TrySetResult(capturedVersionLine);
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                capturedVersionLine = line.Trim();
                // 如果 marker 已经看到，现在收到 version text，返回结果
                if (markerSeen)
                {
                    tcs.TrySetResult(capturedVersionLine);
                }
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



