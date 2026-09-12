using PhotoPrivacy.Core.Pipeline;

using System.Threading.Channels;

namespace PhotoPrivacy.Core.Tests.Pipeline;

/// <summary>
/// ADR 0053 M3 test 闭环: BoundedChannel backpressure + FullMode=Wait behavior.
/// Verifies: channel full → WriteAsync suspends (not OOM); consumer drains → producer resumes.
/// </summary>
public sealed class BackpressureTests
{
    [Fact]
    public async Task Channel_Full_WaitAsync_Suspends_Producer_Until_Consumer_Drains()
    {
        await using var channel = new HotFolderChannel(capacity: 4);

        // Fill channel to capacity (sync writes succeed immediately)
        for (var i = 0; i < 4; i++)
        {
            await channel.WriteAsync($"path-{i}", CancellationToken.None);
        }

        // Next WriteAsync should suspend (channel full, FullMode=Wait)
        var producerTask = channel.WriteAsync("path-overflow", CancellationToken.None);
        await Task.Delay(50, CancellationToken.None); // give producer time to run

        Assert.False(producerTask.IsCompleted);
        Assert.True(producerTask.Status == TaskStatus.WaitingForActivation ||
                    producerTask.Status == TaskStatus.Running ||
                    producerTask.Status == TaskStatus.WaitingToRun);

        // Drain one item → producer resumes
        _ = await channel.Reader.ReadAsync(CancellationToken.None);

        // Producer should now complete within timeout
        await producerTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.True(producerTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Channel_FullMode_Wait_Does_Not_Drop_Items()
        {
        await using var channel = new HotFolderChannel(capacity: 2);
        var consumed = new List<string>();

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var path in channel.Reader.ReadAllAsync(CancellationToken.None))
            {
                consumed.Add(path);
                if (consumed.Count == 5) break;
            }
        });

        for (var i = 0; i < 5; i++)
        {
            await channel.WriteAsync($"item-{i}", CancellationToken.None);
        }
        await consumerTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(5, consumed.Count);
    }

    [Fact]
    public async Task Channel_Complete_Stops_Await_FOREACH()
    {
        await using var channel = new HotFolderChannel(capacity: 4);
        await channel.WriteAsync("first", CancellationToken.None);
        channel.Complete();

        var consumed = new List<string>();
        await foreach (var path in channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            consumed.Add(path);
        }

        Assert.Single(consumed);
        Assert.Equal("first", consumed[0]);
    }

    [Fact]
    public async Task Channel_Writer_Completion_Makes_ReadAllAsync_Exit_Cleanly()
    {
        await using var channel = new HotFolderChannel(capacity: 8);
        channel.Complete();

        var count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            count++;
        }

        Assert.Equal(0, count);
    }
}
