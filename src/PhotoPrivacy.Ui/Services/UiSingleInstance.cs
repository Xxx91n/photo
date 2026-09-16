using System.IO.Pipes;
using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Ui.Services;

public sealed class UiSingleInstance : IDisposable
{
    public const string MutexName = @"Global\PhotoPrivacyUi_Instance";
    internal const string DefaultPipeName = "PhotoPrivacyUi_ShowWindow";
    private const string Message = "SHOW_WINDOW";

    // ADR 0026 (Q7): delegate acquisition to ISingleInstanceGuard (Mutex on Windows, POSIX lockfile on Linux/macOS).
    private readonly ISingleInstanceGuard _guard;

    public UiSingleInstance()
    {
        _guard = SingleInstanceGuardFactory.Create(MutexName);
    }

    public bool IsOwner => _guard.IsOwner;

    public void Dispose()
    {
        _guard.Dispose();
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
                options: ClientPipeOptions);

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
                    // 票 04 / ADR 0067：服务端改用 PipeOptions.CurrentUserOnly 落实「仅当前用户可连」。
                    using var server = CreatePipeServer(listenPipe);

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

    /// <summary>
    /// 创建命名管道服务器。票 04 / ADR 0067：Windows 上以 PipeOptions.CurrentUserOnly 落实
    /// 「仅当前用户可连」（内核在 connect 时校验用户 SID），不再使用 NamedPipeServerStreamAcl.Create ——
    /// 后者在单文件解压上下文抛 UnauthorizedAccessException 并泄漏管道实例（ADR 0035），
    /// 且与 AGENTS.md §5 第 5 条「NamedPipe 不使用 ACL」相矛盾。
    /// 非 Windows 上 .NET 命名管道为 FIFO 实现，不施加该标志以保持既有跨平台语义。
    /// </summary>
    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: ServerPipeOptions);
    }

    /// <summary>服务端管道选项（Windows 上要求同用户对端）。</summary>
    private static PipeOptions ServerPipeOptions => OperatingSystem.IsWindows()
        ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        : PipeOptions.Asynchronous;

    /// <summary>客户端管道选项（Windows 上要求同用户对端）。</summary>
    private static PipeOptions ClientPipeOptions => OperatingSystem.IsWindows()
        ? PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
        : PipeOptions.Asynchronous;
}
