namespace PhotoPrivacy.Ipc;

/// <summary>
/// Factory that creates the correct IIpcTransport based on the current OS.
/// Windows → NamedPipeIpcTransport, Linux/macOS → UnixDomainSocketIpcTransport.
/// </summary>
public static class IpcTransportFactory
{
    /// <summary>
    /// Create a server-side transport for the given endpoint.
    /// On Windows, <paramref name="endpointName"/> is the pipe name.
    /// On Linux/macOS, <paramref name="endpointName"/> is the socket file path.
    /// </summary>
    public static IIpcTransport CreateServer(string endpointName)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new UnixDomainSocketIpcTransport(endpointName);
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
