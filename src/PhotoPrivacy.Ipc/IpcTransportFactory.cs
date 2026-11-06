namespace PhotoPrivacy.Ipc;

/// <summary>
/// Factory that creates the correct IIpcTransport based on the current OS.
/// Windows → NamedPipeIpcTransport, Linux/macOS → UnixDomainSocketIpcTransport.
/// </summary>
public static class IpcTransportFactory
{
    /// <summary>
    /// Create a transport for the given endpoint (background-mode access policy).
    /// On Windows, <paramref name="endpointName"/> is the pipe name.
    /// On Linux/macOS, <paramref name="endpointName"/> is the socket file path.
    /// </summary>
    public static IIpcTransport CreateServer(string endpointName)
    {
        return CreateServer(endpointName, isServiceMode: false);
    }

    /// <summary>
    /// Create a server-side transport with the access policy matching the runtime mode
    /// Unix socket policy: background 0600, service 0660 + group.
    /// Windows pipe policy: background CurrentUserOnly, service installer-SID DACL (#70).
    /// </summary>
    public static IIpcTransport CreateServer(string endpointName, bool isServiceMode)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new UnixDomainSocketIpcTransport(
                endpointName,
                isServiceMode ? UnixSocketAccessMode.ServiceShared : UnixSocketAccessMode.BackgroundOnly);
        }
        return new NamedPipeIpcTransport(
            endpointName,
            isServiceMode ? WindowsPipeAccessMode.ServiceShared : WindowsPipeAccessMode.BackgroundOnly);
    }

    /// <summary>
    /// Resolve the endpoint name based on OS and runtime mode (Service vs Background).
    /// Linux 背景端点随 <c>XDG_RUNTIME_DIR</c> 解析（#201）：缺失/空/非绝对根路径 → null
    /// （端点不可解析）；其余平台/形态恒非 null。调用方对 null 必须 fail-closed
    /// （见 WorkerIpcServerHostedService：非零退出码停宿主，不得静默挂在「活着但没有端点」）。
    /// </summary>
    public static string? ResolveEndpoint(bool isServiceMode)
    {
        if (OperatingSystem.IsLinux())
        {
            return isServiceMode
                ? WorkerIpcEndpointNames.LinuxServiceSocket
                : WorkerIpcEndpointNames.LinuxBackgroundSocket;
        }
        if (OperatingSystem.IsMacOS())
        {
            return isServiceMode
                ? WorkerIpcEndpointNames.MacServiceSocket
                : WorkerIpcEndpointNames.MacBackgroundSocket;
        }
        // Windows
        return isServiceMode
            ? WorkerIpcEndpointNames.ServicePipe
            : WorkerIpcEndpointNames.BackgroundPipe;
    }
}
