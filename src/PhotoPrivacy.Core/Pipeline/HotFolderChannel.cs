using System.Threading.Channels;

namespace PhotoPrivacy.Core.Pipeline;

/// <summary>
/// ADR 0053 M3: BoundedChannel-based file processing pipeline.
/// FSW events → DebounceQueue (dedup/debounce) → HotFolderChannel (backpressure) → consumer.
/// FullMode=Wait means if the channel is full (4096 pending), producers await instead of OOM.
/// </summary>
public sealed class HotFolderChannel : IAsyncDisposable
{
    private readonly Channel<string> _channel;
    private readonly CancellationTokenSource _completionCts = new();

    public HotFolderChannel(int capacity = 4096)
    {
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public ChannelReader<string> Reader => _channel.Reader;
    public ChannelWriter<string> Writer => _channel.Writer;

    public async Task WriteAsync(string path, CancellationToken token)
    {
        await _channel.Writer.WriteAsync(path, token).ConfigureAwait(false);
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
        _completionCts.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _completionCts.Dispose();
        await Task.CompletedTask;
    }
}
