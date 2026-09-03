using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class WorkerIpcClientSourceTests
{
    [Fact]
    public void SendAsync_Source_Should_Use_ConfigureAwaitFalse_For_Awaits()
    {
        var sourcePath = Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Services", "WorkerIpcClient.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        // 使用安全的管道通信：ArgumentList + ReadBoundedLineAsync
        Assert.Contains("await transport.ConnectAsync(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("await stream.WriteAsync(payloadBytes, cancellationToken).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("await stream.FlushAsync(cancellationToken).ConfigureAwait(false);", source, StringComparison.Ordinal);
    }
}
