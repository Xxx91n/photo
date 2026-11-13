using PhotoPrivacy.Core.Pipeline;

using System.Threading.Channels;

namespace PhotoPrivacy.Core.Tests.Pipeline;

/// <summary>
/// BoundedChannel backpressure + FullMode=Wait behavior test 闭环.
/// Verifies: channel full → WriteAsync suspends (not OOM); consumer drains → producer resumes.
/// </summary>
public sealed class BackpressureTests
{
    [Fact]
    public async Task Channel_Full_WaitAsync_Suspends_Producer_Until_Consumer_Drains()
    {
        // Overall test budget: a hang fails fast with a clear cancellation instead of stalling CI.
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = testTimeout.Token;

        await using var channel = new HotFolderChannel(capacity: 4);
        // Fill channel to capacity (sync writes succeed immediately)
        for (var i = 0; i < 4; i++)
        {
            await channel.WriteAsync($"path-{i}", ct);
        }
        // Next WriteAsync should suspend (channel full, FullMode=Wait).
        // Observable signal instead of a fixed sleep + Task.Status peeking:
        // while nobody drains the channel, the producer must stay incomplete
        // for the whole observation window. If it finishes first, backpressure is broken.
        var producerTask = channel.WriteAsync("path-overflow", ct);
        var observationWindow = Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        var firstFinished = await Task.WhenAny(producerTask, observationWindow);
        Assert.Same(observationWindow, firstFinished);
        // Drain one item → producer resumes and completes within the test budget.
        _ = await channel.Reader.ReadAsync(ct);
        await producerTask.WaitAsync(ct);
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
