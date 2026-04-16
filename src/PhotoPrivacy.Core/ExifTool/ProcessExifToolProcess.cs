using System.Diagnostics;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ProcessExifToolProcess : IExifToolProcess
{
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutPumpTask;

    public event Action<string>? StdoutLine;

    public Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
    {
        if (_process is not null)
        {
            return Task.CompletedTask;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Start();

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;

        _stdoutPumpTask = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _process is { HasExited: false })
            {
                var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                StdoutLine?.Invoke(line);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public async Task WriteStdinAsync(string text, CancellationToken cancellationToken)
    {
        if (_stdin is null)
        {
            throw new InvalidOperationException("ExifTool process not started.");
        }

        await _stdin.WriteAsync(text.AsMemory(), cancellationToken);
        await _stdin.FlushAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (_stdin is not null)
            {
                await _stdin.WriteAsync("-stay_open\nFalse\n-execute\n");
                await _stdin.FlushAsync(cancellationToken);
            }
        }
        catch
        {
            // ignore and fallback to kill below
        }

        if (!_process.WaitForExit(2000))
        {
            _process.Kill(entireProcessTree: true);
        }

        if (_stdoutPumpTask is not null)
        {
            try
            {
                await _stdoutPumpTask.WaitAsync(cancellationToken);
            }
            catch
            {
                // ignore shutdown race
            }
        }

        _stdin?.Dispose();
        _process.Dispose();

        _stdin = null;
        _process = null;
        _stdoutPumpTask = null;
    }
}
