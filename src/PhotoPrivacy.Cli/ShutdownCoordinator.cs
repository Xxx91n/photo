using System.Threading;

namespace PhotoPrivacy.Cli;

public sealed class ShutdownCoordinator
{
    private readonly Func<CancellationToken, Task> _stopHostAsync;
    private readonly TimeSpan _stopTimeout;
    private int _requested;
    private readonly TaskCompletionSource<bool> _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ShutdownCoordinator(Func<CancellationToken, Task> stopHostAsync, TimeSpan stopTimeout)
    {
        _stopHostAsync = stopHostAsync;
        _stopTimeout = stopTimeout;
    }

    public void RequestStop()
    {
        if (Interlocked.Exchange(ref _requested, 1) == 1)
        {
            return;
        }

        _firstRequest.TrySetResult(true);

        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(_stopTimeout);
            try
            {
                await _stopHostAsync(cts.Token);
            }
            catch
            {
                // shutdown path must not throw to signal handlers
            }
        });
    }

    public Task WaitForFirstRequestAsync(TimeSpan timeout)
    {
        return _firstRequest.Task.WaitAsync(timeout);
    }
}
