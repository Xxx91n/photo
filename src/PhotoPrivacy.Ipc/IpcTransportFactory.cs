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
    /// (ADR 0067: background 0600 / service 0660+group on Unix).
    /// </summary>
    public static IIpcTransport CreateServer(string endpointName, bool isServiceMode)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new UnixDomainSocketIpcTransport(
                endpointName,
                isServiceMode ? UnixSocketAccessMode.ServiceShared : UnixSocketAccessMode.BackgroundOnly);
        }
        return new NamedPipeIpcTransport(endpointName);
    }

    /// <summary>
    /// Resolve the endpoint name based on OS and runtime mode (Service vs Background).
    /// </summary>
    public static string ResolveEndpoint(bool isServiceMode)
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
