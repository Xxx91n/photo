using System.IO.Pipes;

namespace PhotoPrivacy.Cli;

public static class SingleInstanceIpc
{
    public const string PipeName = "PhotoPrivacyCleaner_ShowWindow";
    public const string ShowWindowMessage = "SHOW_WINDOW";

    public static async Task<bool> TryNotifyRunningInstanceToShowWindowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            await client.ConnectAsync(timeoutCts.Token);

            using var writer = new StreamWriter(client) { AutoFlush = true };
            await writer.WriteLineAsync(ShowWindowMessage.AsMemory(), timeoutCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Task RunShowWindowServerAsync(Action onShowWindowRequested, CancellationToken cancellationToken)
    {
        if (onShowWindowRequested is null)
        {
            throw new ArgumentNullException(nameof(onShowWindowRequested));
        }

        return Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        pipeName: PipeName,
                        direction: PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        transmissionMode: PipeTransmissionMode.Byte,
                        options: PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(cancellationToken);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (string.Equals(line, ShowWindowMessage, StringComparison.Ordinal))
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
