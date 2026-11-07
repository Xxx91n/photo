using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Runtime;

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

    /// <summary>
    /// 停机拆卸闸（#37）：StopAsync 串行化——并发/重复进入只放第一遍做完整拆卸，后续立即返回。
    /// 没有这道闸，两遍拆卸同时读到非 null 的 <c>_process</c> 会各自 Dispose 同一对象，
    /// 第二遍抛 ObjectDisposedException 逃到停机路径变成 Warning 噪声。
    /// StartAsync 重置为 0；拆卸中途失败也复位（rethrow 给调用方留痕，重试不被闸挡死）。
    /// 同族竞态：ExifToolBridge._stdinGate 的 Dispose/Release（其 gate 所有权已随 #126 收口）。
    /// </summary>
    private int _stopGate;

    private readonly ILogger? _logger;

    public ProcessExifToolProcess()
        : this(logger: null)
    {
    }

    public ProcessExifToolProcess(IProcessJobObject jobObject)
    {
        _jobObject = jobObject;
        _logger = null;
    }

    public ProcessExifToolProcess(ILogger? logger)
    {
        _jobObject = CreateDefaultProcessJobObject();
        _logger = logger;
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
        // 重启复用同一实例（StopAsync → StartAsync）：重置停机闸，见 _stopGate 注释。
        Volatile.Write(ref _stopGate, 0);
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
        // 停机闸（#37）：并发/重复 StopAsync 只放第一遍做完整拆卸，后续立即返回。
        // 拆卸是终态收尾（字段清 null、job object 释放），第二遍除了撞已 Dispose 的对象无事可做。
        if (Interlocked.Exchange(ref _stopGate, 1) != 0)
        {
            return;
        }

        try
        {
            var stopped = Volatile.Read(ref _process);
            if (stopped is null)
            {
                _jobObject?.Dispose();
                _jobObject = null;
                Volatile.Write(ref _stopGate, 0);
                return;
            }

            // 1. Ask ExifTool to exit gracefully.
            // 写入/flush 受同一 3 s 预算约束（CodeRabbit #228 二轮）：无令牌的 WriteAsync(string)
            // 在对端不读且缓冲写满时会把停机吊死在强杀逻辑之前；先建 exitCts 再写，
            // 写入、等待退出、强杀共享同一预算，被超时打断时 OCE 进分级留痕（Warning，真故障信号）。
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                if (_stdin is not null)
                {
                    await _stdin.WriteAsync("-stay_open\nFalse\n-execute\n".AsMemory(), exitCts.Token);
                    await _stdin.FlushAsync(exitCts.Token);
                }
            }
            catch (Exception ex)
            {
                // 停机期 best-effort 写尾（#37）：进程已死/管道已断（Pipe is broken）是预期形态，
                // 降 Debug 不进停机日志；其余异常同样不上抛（停机收尾语义不变）但留 Warning 痕，
                // 不再无痕吞掉。
                LogStdinTeardownFailure(ex, "asking ExifTool to exit");
            }

            // 2. Wait up to 3 s asynchronously, then force-kill.
            //    Use a linked token so either the caller's timeout or our own fires first.
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
            // stdin StreamWriter.Dispose 在进程已死时会抛 IOException: Pipe is broken（#37，
            // ui-smoke.md L 组 L9 段）——停机预期形态，降 Debug 不进停机日志；
            // 其余异常留 Warning 痕后继续拆卸（停机收尾不阻断，与 StopBridgeAsync 口径一致）。
            try
            {
                _stdin?.Dispose();
            }
            catch (Exception ex)
            {
                LogStdinTeardownFailure(ex, "disposing ExifTool stdin");
            }

            _pumpCts?.Dispose();
            stopped.Dispose();

            _stdin = null;
            _pumpCts = null;
            _process = null;
            _stdoutPumpTask = null;
            _stderrPumpTask = null;
            _lastStderrLine = null;
        }
        catch
        {
            // 拆卸中途失败：复位闸避免后续 StopAsync 被闸挡成静默空转；部分拆卸的对象态
            // （已 Dispose 的 process/job、字段未清）不可完全恢复，重试可能仍失败——但每次
            // 都带痕上抛由停机路径既有 catch 留 Warning，进程随 job object/进程退出收尾。
            Volatile.Write(ref _stopGate, 0);
            throw;
        }
    }

    /// <summary>
    /// 停机期 stdin 收尾异常分级（#37）：管道已断/对象已释放是预期形态，降 Debug 不进停机日志；
    /// 其余异常留 Warning 痕——分级去重共用一个出口，不再无痕吞掉。
    /// 留痕自身走 SafeLog.Try：日志器记录时抛错不得中断停机拆卸（CodeRabbit #228）。
    /// </summary>
    private void LogStdinTeardownFailure(Exception ex, string stage)
    {
        SafeLog.Try(() =>
        {
            if (ex is IOException or ObjectDisposedException)
            {
                _logger?.LogDebug(ex, "stdin already unavailable while {Stage} (expected during shutdown).", stage);
            }
            else
            {
                _logger?.LogWarning(ex, "Unexpected failure while {Stage}; continuing teardown.", stage);
            }
        });
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
