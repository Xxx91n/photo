using System.Threading.Channels;

namespace PhotoPrivacy.Core.Pipeline;

/// <summary>
/// ADR 0053 M3: BoundedChannel-based file processing pipeline.
/// FSW events → DebounceQueue (dedup/debounce) → HotFolderChannel (backpressure) → consumer.
/// FullMode=Wait means if the channel is full (4096 pending), producers await instead of OOM.
/// </summary>
// ponytail: M3 Channel not yet integrated into MetadataCleanerWorker — DebounceQueue stable at max_parallel:1.
// Ceiling: high-concurrency workload >1 parallel ExifTool. Upgrade: integrate into MetadataCleanerWorker.ExecuteAsync as producer/consumer.
// ADR 0053 declares this as deferred (milestone acceptance met with BackpressureTests only).
public sealed class HotFolderChannel : IAsyncDisposable
{
    private readonly Channel<string> _channel;

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
    }

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
