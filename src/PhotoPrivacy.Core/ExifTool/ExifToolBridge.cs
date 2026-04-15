using System.Collections.Concurrent;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ExifToolBridge : IExifToolBridge
{
    private readonly IExifToolProcess _process;
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private int _taskId;

    public ExifToolBridge(IExifToolProcess process, AppConfig config)
    {
        _process = process;
        _config = config;
        _process.StdoutLine += OnStdoutLine;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _process.StartAsync(
            _config.ExifTool.Path,
            ExifToolCommandBuilder.BuildStartArguments(_config),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _process.StopAsync(cancellationToken);
    }

    public async Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _taskId).ToString();
        var marker = $"TASK_DONE_{id}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[marker] = tcs;

        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(targetPath, id);
        await _process.WriteStdinAsync(block, cancellationToken);
        await tcs.Task.WaitAsync(cancellationToken);
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
