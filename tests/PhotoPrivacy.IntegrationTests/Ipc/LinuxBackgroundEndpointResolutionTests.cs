using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

public sealed class LinuxBackgroundEndpointResolutionTests
{
    [Fact]
    public void BackgroundSocket_Follows_User_Runtime_Directory()
    {
        // 防（#201）：回潮到普通用户无权创建的 /run/photoprivacy；与 Rust 纯函数测试对拍。
        // 生产仅 Linux 使用；Windows CI 也跑纯函数，故比较时归一分隔符。
        Assert.Equal(
            "/run/user/1000/photoprivacy/worker-background.sock",
            WorkerIpcEndpointNames.ResolveLinuxBackgroundSocket("/run/user/1000")?.Replace('\\', '/'));
    }

    [Fact]
    public async Task BackgroundSocket_Binds_In_User_Writable_Runtime_Directory()
    {
        // 防（#201）：只改路径字符串却仍依赖 root 创建目录；在无预建子目录的用户临时目录真实绑定。
        if (!OperatingSystem.IsLinux()) return;

        var runtimeDirectory = Path.Combine(Path.GetTempPath(), "pp201" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(runtimeDirectory);
        try
        {
            var endpoint = WorkerIpcEndpointNames.ResolveLinuxBackgroundSocket(runtimeDirectory);
            Assert.NotNull(endpoint);
            await using var transport = IpcTransportFactory.CreateServer(endpoint, isServiceMode: false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await transport.ListenAsync(timeout.Token);
            Assert.True(File.Exists(endpoint));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(endpoint));
            using var client = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Unspecified);
            await client.ConnectAsync(new System.Net.Sockets.UnixDomainSocketEndPoint(endpoint), timeout.Token);
            await using var accepted = await transport.AcceptClientAsync(timeout.Token);
            Assert.True(accepted.CanRead);
        }
        finally
        {
            Directory.Delete(runtimeDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("run/user/1000")]
    [InlineData("relative")]
    [InlineData(" /run/user/1000")]
    public void BackgroundSocket_Unavailable_Without_Rooted_Runtime_Directory(string? directory)
    {
        // 防（#201）：环境缺失时静默回落或拼出相对端点，导致 UI/Worker 解析分裂。
        Assert.Null(WorkerIpcEndpointNames.ResolveLinuxBackgroundSocket(directory));
    }

    [Fact]
    public void BackgroundSocket_Reads_Current_Environment()
    {
        // 防（#201）：缓存环境解析使父子进程落点不一致。程序集已禁用测试并行，始终恢复环境。
        var previous = Environment.GetEnvironmentVariable(WorkerIpcEndpointNames.LinuxRuntimeDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(WorkerIpcEndpointNames.LinuxRuntimeDirectoryVariable, "/run/user/4242");
            Assert.Equal(
                "/run/user/4242/photoprivacy/worker-background.sock",
                WorkerIpcEndpointNames.LinuxBackgroundSocket?.Replace('\\', '/'));
            Environment.SetEnvironmentVariable(WorkerIpcEndpointNames.LinuxRuntimeDirectoryVariable, null);
            Assert.Null(WorkerIpcEndpointNames.LinuxBackgroundSocket);
        }
        finally
        {
            Environment.SetEnvironmentVariable(WorkerIpcEndpointNames.LinuxRuntimeDirectoryVariable, previous);
        }
    }
}
