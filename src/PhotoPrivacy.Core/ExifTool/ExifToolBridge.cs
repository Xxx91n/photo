using System.Collections.Concurrent;
using System.Text;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ExifToolBridge : IExifToolBridge
{
    private readonly IExifToolProcess _process;
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private readonly object _lifecycleGate = new();
    private readonly TimeSpan _healthTimeout;

    private bool _started;
    private int _taskId;

    public ExifToolBridge(IExifToolProcess process, AppConfig config, TimeSpan? healthTimeout = null)
    {
        _process = process;
        _config = config;
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
