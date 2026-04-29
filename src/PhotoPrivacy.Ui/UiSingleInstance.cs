using System.IO.Pipes;

namespace PhotoPrivacy.Ui;

public sealed class UiSingleInstance : IDisposable
{
    public const string MutexName = @"Global\PhotoPrivacyUi_Instance";
    internal const string DefaultPipeName = "PhotoPrivacyUi_ShowWindow";
    private const string Message = "SHOW_WINDOW";

    private readonly Mutex _mutex;
    private readonly bool _isOwner;

    public UiSingleInstance()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out var isOwner);
            _isOwner = isOwner;
        }
        catch (AbandonedMutexException)
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out _);
            _isOwner = true;
        }
    }

    public bool IsOwner => _isOwner;

    public void Dispose()
    {
        _mutex.Dispose();
    }

    public static async Task<bool> NotifyExistingInstanceAsync(CancellationToken cancellationToken = default, string? pipeName = null)
    {
        try
        {
            var targetPipe = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: targetPipe,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync(timeoutCts.Token);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(Message.AsMemory(), timeoutCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Task RunShowWindowServerAsync(
        Action onShowWindowRequested,
        CancellationToken cancellationToken,
        string? pipeName = null)
    {
        return Task.Run(async () =>
        {
            var listenPipe = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName: listenPipe,
                        direction: PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Byte,
                        options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.Equals(line, Message, StringComparison.Ordinal))
                    {
                        onShowWindowRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
        }, cancellationToken);
    }
}
