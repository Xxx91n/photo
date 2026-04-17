using System.Diagnostics;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ProcessExifToolProcess : IExifToolProcess
{
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutPumpTask;
    private Task? _stderrPumpTask;
    private CancellationTokenSource? _pumpCts;
    private string? _lastStderrLine;

    public event Action<string>? StdoutLine;

    public bool IsRunning => _process is { HasExited: false };

    public string? LastStderrLine => Volatile.Read(ref _lastStderrLine);

    public Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
    {
        if (_process is not null)
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("ExifTool executable not found.", exePath);
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

        // Use a dedicated CTS so the pump loop is not tied to the host
        // cancellation token — this lets us cancel it explicitly on stop.
        _pumpCts = new CancellationTokenSource();
        var pumpToken = _pumpCts.Token;

        _stdoutPumpTask = Task.Run(async () =>
        {
            try
            {
                while (!pumpToken.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(pumpToken);
                    if (line is null)
                    {
                        break;
                    }

                    StdoutLine?.Invoke(line);
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown path
            }
        }, pumpToken);

        _stderrPumpTask = Task.Run(async () =>
        {
            try
            {
                while (!pumpToken.IsCancellationRequested)
                {
                    var line = await _process.StandardError.ReadLineAsync(pumpToken);
                    if (line is null)
                    {
                        break;
                    }

                    Volatile.Write(ref _lastStderrLine, line);
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown path
            }
        }, pumpToken);

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

        // 1. Ask ExifTool to exit gracefully.
        try
        {
            if (_stdin is not null)
            {
                await _stdin.WriteAsync("-stay_open\nFalse\n-execute\n");
                await _stdin.FlushAsync(CancellationToken.None);
            }
        }
        catch
        {
            // ignore — process may already be dead
        }

        // 2. Wait up to 3 s asynchronously, then force-kill.
        //    Use a linked token so either the caller's timeout or our own fires first.
        using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(exitCts.Token, cancellationToken);
        try
        {
            await _process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Timed out or caller cancelled — force-kill the process.
            try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        // 3. Cancel the pump loops and wait with a short hard timeout.
        if (_pumpCts is not null)
        {
            await _pumpCts.CancelAsync();
        }

        if (_stdoutPumpTask is not null)
        {
            try
            {
                await _stdoutPumpTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // timeout or cancellation — acceptable
            }
        }

        if (_stderrPumpTask is not null)
        {
            try
            {
                await _stderrPumpTask.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // timeout or cancellation — acceptable
            }
        }

        // 4. Clean up.
        _stdin?.Dispose();
        _pumpCts?.Dispose();
        _process.Dispose();

        _stdin = null;
        _pumpCts = null;
        _process = null;
        _stdoutPumpTask = null;
        _stderrPumpTask = null;
        _lastStderrLine = null;
    }
}
