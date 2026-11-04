using System.IO.Pipes;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 行为测试：后台管道的服务端进程身份核验（#7 残余①）。
/// CI 平台限制（如实登记）：test job 在 ubuntu-latest 上覆盖跨平台 API 形状与期望基准校验；
/// 真实管道核验（GetNamedPipeServerProcessId / OpenProcess）是 Windows 专属，在非 Windows
/// 宿主上跳过，Windows 实机由 S4 复测承担（同 #70/#7 既有口径）。
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
        // 防（#7 残余①回潮）：同用户抢先建的管道被放行——PID 不符且镜像不是期望 Worker 时
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
        var name = "PhotoPrivacyTest.Identity." + Guid.NewGuid().ToString("N");
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
