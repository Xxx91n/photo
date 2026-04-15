using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolBridgeTests
{
    [Fact]
    public async Task WipeMetadataAsync_Should_Complete_When_TaskDone_Line_Arrives()
    {
        var process = new FakeExifToolProcess();
        var bridge = new ExifToolBridge(process, AppConfig.Default);
        await bridge.StartAsync(CancellationToken.None);

        var task = bridge.WipeMetadataAsync(@"D:\hot\a.jpg", CancellationToken.None);

        await Task.Delay(20);
        process.EmitStdout("TASK_DONE_1");

        await task;
        Assert.Single(process.Writes);
        Assert.Contains("-echo1 TASK_DONE_1", process.Writes[0], StringComparison.Ordinal);
    }

    private sealed class FakeExifToolProcess : IExifToolProcess
    {
        public event Action<string>? StdoutLine;

        public string ExePath { get; private set; } = string.Empty;
        public IReadOnlyList<string> Args => _args;
        public List<string> Writes { get; } = [];

        private readonly List<string> _args = [];

        public Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
        {
            ExePath = exePath;
            _args.Clear();
            _args.AddRange(args);
            return Task.CompletedTask;
        }

        public Task WriteStdinAsync(string text, CancellationToken cancellationToken)
        {
            Writes.Add(text);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void EmitStdout(string line)
        {
            StdoutLine?.Invoke(line);
        }
    }
}
