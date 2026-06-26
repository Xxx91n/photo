using System.Text;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class WorkerIpcClientSourceTests
{
    [Fact]
    public void SendAsync_Source_Should_Use_ConfigureAwaitFalse_For_Awaits()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "WorkerIpcClient.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        // 使用安全的管道通信：ArgumentList + ReadBoundedLineAsync
        Assert.Contains("await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("await client.WriteAsync(payloadBytes, timeoutCts.Token).ConfigureAwait(false);", source, StringComparison.Ordinal);
        Assert.Contains("await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);", source, StringComparison.Ordinal);
    }
}
