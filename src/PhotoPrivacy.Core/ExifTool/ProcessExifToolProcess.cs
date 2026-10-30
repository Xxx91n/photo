using System.Diagnostics;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ProcessExifToolProcess : IExifToolProcess
{
    private IProcessJobObject? _jobObject;
    private Process? _process;
    private StreamWriter? _stdin;
    private Task? _stdoutPumpTask;
    private Task? _stderrPumpTask;
    private CancellationTokenSource? _pumpCts;
    private string? _lastStderrLine;
    private int _exitSignaled;

    public ProcessExifToolProcess()
        : this(CreateDefaultProcessJobObject())
    {
    }

    public ProcessExifToolProcess(IProcessJobObject jobObject)
    {
        _jobObject = jobObject;
    }

    public static IProcessJobObject CreateDefaultProcessJobObject()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NoopProcessJobObject();
        }

        return new ProcessJobObject();
    }

    public event Action<string>? StdoutLine;

    public event Action? ProcessExited;

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

        _jobObject ??= CreateDefaultProcessJobObject();

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

        Volatile.Write(ref _exitSignaled, 0);
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.Start();
        _jobObject.Assign(_process);
        var generation = _process;

        // Subscribe to Exited event for zombie reap on Unix
        // Process.EnableRaisingEvents is already true above; this ensures we reap the child.
        generation.Exited += (_, _) =>
        {
            try { _ = generation.WaitForExit(0); } catch { /* already exited */ }

            SignalExited(generation);
        };

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
                        // stdout EOF：子进程的写端已关闭，协议流不会再有 {ready}。
                        SignalExited(generation);
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
        var stopped = Volatile.Read(ref _process);
        if (stopped is null)
        {
            _jobObject?.Dispose();
            _jobObject = null;
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
            await stopped.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Timed out or caller cancelled — force-kill the process.
            try { stopped.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        // 停机到此已确定进程没了。不依赖 Process.Exited 是否已派发（Dispose 之后它可能永远不会来）。
        SignalExited(stopped);

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

        // 4. Dispose job object only after process has been stopped.
        _jobObject?.Dispose();
        _jobObject = null;

        // 5. Clean up remaining resources.
        _stdin?.Dispose();
        _pumpCts?.Dispose();
        stopped.Dispose();

        _stdin = null;
        _pumpCts = null;
        _process = null;
        _stdoutPumpTask = null;
        _stderrPumpTask = null;
        _lastStderrLine = null;
    }

    /// <summary>
    /// 每一代进程最多上报一次退出。来源有三：stdout EOF、<c>Process.Exited</c>、本类 <c>StopAsync</c>
    /// 收尾——它们说的都是同一件事：这一代的进程已经没了，协议流不会再有 {ready}。
    /// </summary>
    private void SignalExited(Process generation)
    {
        // 只认当代那个进程对象：上一代的 Exited/EOF 回调可能在新一代已经跑起来之后才派发，
        // 照它发信号会把新一代正在正常处理的擦除判成「进程没了」。
        if (!ReferenceEquals(Volatile.Read(ref _process), generation))
        {
            return;
        }

        if (Interlocked.Exchange(ref _exitSignaled, 1) != 0)
        {
            return;
        }

        ProcessExited?.Invoke();
    }
}
