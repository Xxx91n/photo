using PhotoPrivacy.Core.ExifTool;
using Xunit;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ProcessExifToolProcessTests
{
    [Fact]
    public async Task StartAsync_Should_Throw_When_Executable_Not_Found()
    {
        var process = new ProcessExifToolProcess();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            process.StartAsync(@"D:\missing\ExifTool.exe", [], CancellationToken.None));
    }

    [Fact]
    public void CreateDefaultProcessJobObject_Should_Return_Noop_On_NonWindows()
    {
        var jobObject = ProcessExifToolProcess.CreateDefaultProcessJobObject();

        if (OperatingSystem.IsWindows())
        {
            Assert.False(jobObject is NoopProcessJobObject);
        }
        else
        {
            Assert.IsType<NoopProcessJobObject>(jobObject);
        }
    }

    [Fact]
    public async Task StopAsync_Should_Dispose_Injected_JobObject()
    {
        var job = new SpyJobObject();
        var process = new ProcessExifToolProcess(job);

        await process.StopAsync(CancellationToken.None);

        Assert.True(job.Disposed);
    }

    [Fact]
    public async Task StopAsync_Then_StartAsync_Should_Not_Throw()
    {
        var exePath = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        var job = new ThrowOnDisposedJobObject();
        var process = new ProcessExifToolProcess(job);

        await process.StartAsync(exePath, ["/c", "exit 0"], CancellationToken.None);
        await process.StopAsync(CancellationToken.None);

        var exception = await Record.ExceptionAsync(
            () => process.StartAsync(exePath, ["/c", "exit 0"], CancellationToken.None));

        Assert.Null(exception);
        await process.StopAsync(CancellationToken.None);
    }

    // 要防的老毛病：停机拆卸没有闸时，停机两遍拆卸（#126 形态）或重载拆卸与停机拆卸并发，
    // 各自读到非 null 的 _process 后对同一 Process/CTS/job object 二次 Dispose，
    // ObjectDisposedException 逃到停机路径变成 StopBridgeAsync 的 Warning 噪声。
    // 关联 issue #37。
    [Fact]
    public async Task Concurrent_StopAsync_Should_Teardown_Exactly_Once_And_Not_Throw()
    {
        var exePath = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        var job = new SpyJobObject();
        var process = new ProcessExifToolProcess(job);
        await process.StartAsync(exePath, ["/c", "ping -n 30 127.0.0.1 > nul"], CancellationToken.None);

        var first = process.StopAsync(CancellationToken.None);
        var second = process.StopAsync(CancellationToken.None);
        var exception = await Record.ExceptionAsync(() => Task.WhenAll(first, second));

        Assert.Null(exception);
        Assert.Equal(1, job.DisposeCount);
        Assert.False(process.IsRunning);
    }

    // 防的具体退化形态：ExifTool 已自行退出（崩溃/被杀）后停机，stdin 的 -stay_open False 写尾
    // 与 StreamWriter.Dispose 会撞 Pipe is broken——该 IO 异常曾经无痕逃出 StopAsync，
    // 落成停机路径的 Warning 噪声（#31 轮 2026-11-04 实机观察，原记录已随 #254 移入 git 历史）。停机必须安静完成。
    // 关联 issue #37。
    [Fact]
    public async Task StopAsync_After_Process_Already_Exited_Should_Complete_Quietly()
    {
        var exePath = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        var job = new SpyJobObject();
        var process = new ProcessExifToolProcess(job);
        await process.StartAsync(exePath, ["/c", "exit 0"], CancellationToken.None);

        // 等子进程真退出，让 stdin 管道进入 broken 状态后再停机。
        await Task.Delay(500);
        var stopped = await Record.ExceptionAsync(() => process.StopAsync(CancellationToken.None));

        Assert.Null(stopped);
        Assert.True(job.Disposed);
    }

    private sealed class SpyJobObject : IProcessJobObject
    {
        public bool Disposed => DisposeCount > 0;

        public int DisposeCount { get; private set; }

        public void Assign(System.Diagnostics.Process process)
        {
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class ThrowOnDisposedJobObject : IProcessJobObject
    {
        private bool _disposed;

        public void Assign(System.Diagnostics.Process process)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ThrowOnDisposedJobObject));
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
