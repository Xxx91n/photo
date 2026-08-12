using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Ui;

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
                    // 使用带访问控制的管道，限制只有当前用户可以连接。
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
    /// 创建带访问控制的命名管道服务器。限制只有当前用户可以连接。
    /// </summary>
    private static NamedPipeServerStream CreatePipeServer(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var pipeSecurity = new PipeSecurity();
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser is not null)
                {
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        currentUser,
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));
                }

                return NamedPipeServerStreamAcl.Create(
                    pipeName: pipeName,
                    direction: PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,
                    options: PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity);
            }
            catch
            {
                // 回退：如果 ACL 操作失败（如容器环境），使用无 ACL 版本
            }
        }

        return new NamedPipeServerStream(
            pipeName: pipeName,
            direction: PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);
    }
}
