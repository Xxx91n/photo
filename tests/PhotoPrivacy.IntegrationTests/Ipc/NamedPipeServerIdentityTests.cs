using System.IO.Pipes;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 行为测试：后台管道的服务端进程身份核验（#7 残余①）。
/// CI 平台（如实登记，#9 起多平台矩阵）：test job 在 ubuntu/windows/macos 三平台跑——
/// 跨平台 API 形状与期望基准校验随主矩阵全平台真跑（Windows 上走真实管道核验）；
/// GetNamedPipeServerProcessId / OpenProcess 的 Windows 实机验收由 S4 复测承担
/// （同 #70/#7 既有规则）。
/// </summary>
public sealed class NamedPipeServerIdentityTests
{
    [Fact]
    public void Expectation_Rejects_NonPositive_ProcessId()
    {
        // 防：把 0/负数当「有效期望 PID」——0 在 Windows 上表示无效句柄/PID，
        // 会让核验退化成「永不匹配」或静默放行（#7）。
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerProcessIdentityExpectation(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServerProcessIdentityExpectation(-1));
    }

    [Fact]
    public void Server_Process_Probe_Is_Inert_Off_Windows()
    {
        // 防：平台能力缺口被当成核验成功——非 Windows 上取不到管道服务端 PID，
        // 必须返回 false（C# 传输层按 fail-closed 处置：核验不可用即拒连），不能假装已核验（#7 AC2）。
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var (server, client) = CreateConnectedPair();
        using (server)
        using (client)
        {
            Assert.False(NamedPipeServerIdentity.TryGetServerProcessId(client.SafePipeHandle, out var pid));
            Assert.Equal(0, pid);
            Assert.False(NamedPipeServerIdentity.TryGetProcessImagePath(Environment.ProcessId, out var image));
            Assert.Null(image);
        }
    }

    [Fact]
    public void Verify_Accepts_Server_Whose_Pid_Matches_Expectation()
    {
        // 防（#7 AC1）：UI 拉起的 Worker 被核验器误拒——PID 直配必须放行，且不需要镜像查询。
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (server, client) = CreateConnectedPair();
        using (server)
        using (client)
        {
            var expectation = new ServerProcessIdentityExpectation(expectedProcessId: Environment.ProcessId);
            Assert.True(
                NamedPipeServerIdentity.Verify(client.SafePipeHandle, expectation, out var failure),
                failure);
        }
    }

    [Fact]
    public void Verify_Rejects_Foreign_Server_Image()
    {
        // 防（#7 残余①老毛病再犯）：同用户抢先建的管道被放行——PID 不符且镜像不是期望 Worker 时
        // 必须拒绝，并把原因（PID + 实际判定）带回给调用方。
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (server, client) = CreateConnectedPair();
        using (server)
        using (client)
        {
            var foreignImage = Path.Combine(Path.GetTempPath(), "not-the-worker.exe");
            var expectation = new ServerProcessIdentityExpectation(
                expectedProcessId: 1,
                expectedImagePath: foreignImage);

            Assert.False(NamedPipeServerIdentity.Verify(client.SafePipeHandle, expectation, out var failure));
            Assert.Contains("不是期望的", failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Verify_Rejects_When_No_Expectation_Is_Provided()
    {
        // 防（#7）：无期望基准时静默放行——核验器必须 fail-closed；「不注入期望 = 不核验」
        // 是构造期选择，不是运行期放行。
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var (server, client) = CreateConnectedPair();
        using (server)
        using (client)
        {
            Assert.False(
                NamedPipeServerIdentity.Verify(
                    client.SafePipeHandle,
                    new ServerProcessIdentityExpectation(),
                    out var failure));
            Assert.Contains("无核验基准", failure, StringComparison.Ordinal);
        }
    }

    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) CreateConnectedPair()
    {
        // macOS UDS 路径上限 104 字节（含 NUL）：NamedPipeServerStream 在 macOS 落 TMPDIR
        // （约 49 字节）+ 本前缀 26 字节，随机段必须短——32 字符 GUID 段在 mac 实跑超限
        // （#9 矩阵 mac 腿红实证，run 41964334172）；与 IpcAuthenticationLayeringTests/
        // WorkerReloadConfigRegressionTests 的短段先例同一规则。
        var name = "PhotoPrivacyTest.Identity." + Guid.NewGuid().ToString("N")[..8];
        var server = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            client.Connect(5000);
            server.WaitForConnection();
            return (server, client);
        }
        catch
        {
            client.Dispose();
            server.Dispose();
            throw;
        }
    }
}
