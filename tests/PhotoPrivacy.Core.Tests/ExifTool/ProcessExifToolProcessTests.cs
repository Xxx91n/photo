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

    private sealed class SpyJobObject : IProcessJobObject
    {
        public bool Disposed { get; private set; }

        public void Assign(System.Diagnostics.Process process)
        {
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
