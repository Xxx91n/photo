using System.IO.Pipes;

namespace PhotoPrivacy.Worker;

public static class UiSingleInstanceIpc
{
    private const string PipeName = "PhotoPrivacyUi_ShowWindow";
    private const string Message = "SHOW_WINDOW";

    /// <summary>
    /// 票 04 / ADR 0067：Windows 上以 PipeOptions.CurrentUserOnly 与 UI 侧服务端同享
    /// 「仅当前用户可连」边界；非 Windows 保持既有无标志语义。
    /// </summary>
    private static PipeOptions ClientOptions => OperatingSystem.IsWindows()
        ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        : PipeOptions.Asynchronous;

    public static async Task<bool> TryNotifyUiToShowWindowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: PipeName,
                direction: PipeDirection.Out,
                options: ClientOptions);

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
