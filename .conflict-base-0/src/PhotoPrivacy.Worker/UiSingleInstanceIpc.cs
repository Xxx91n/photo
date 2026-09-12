using System.IO.Pipes;

namespace PhotoPrivacy.Worker;

public static class UiSingleInstanceIpc
{
    private const string PipeName = "PhotoPrivacyUi_ShowWindow";
    private const string Message = "SHOW_WINDOW";

    public static async Task<bool> TryNotifyUiToShowWindowAsync(CancellationToken cancellationToken = default)
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
            await writer.WriteLineAsync(Message.AsMemory(), timeoutCts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
