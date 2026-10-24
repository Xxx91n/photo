namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// IPC 与部署安全不变量守卫：每条断言都直接对账代码/脚本/配置，改措辞不会让它变红。
/// 1) UiSingleInstance 不得重新调用 NamedPipeServerStreamAcl / System.Security.AccessControl，
///    且必须保留 PipeOptions.CurrentUserOnly（同用户边界）；
/// 2) install-systemd-service.sh 的 unit 模板必须钉住 RuntimeDirectoryMode=0750 并提供 GUI 用户组供给；
/// 3) NamedPipeIpcTransport 的 FirstPipeInstance 仅施加于 ListenAsync 预建的首实例。
/// 每组守卫都带 negative self-proof：把源码改坏后备守卫必须变红，防止守卫空转。
/// </summary>
public sealed class IpcAuthClaimReconciliationTests
{
    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    // ---------- 1. UiSingleInstance 不使用 ACL 管道 API ----------

    internal static List<string> ClassifyUiSingleInstance(string strippedSource)
    {
        var violations = new List<string>();
        if (strippedSource.Contains("NamedPipeServerStreamAcl", StringComparison.Ordinal))
        {
            violations.Add("UiSingleInstance 重新调用了 NamedPipeServerStreamAcl（该 API 会泄漏管道 ACL 边界）");
        }
        if (strippedSource.Contains("System.Security.AccessControl", StringComparison.Ordinal))
        {
            violations.Add("UiSingleInstance 重新引入 System.Security.AccessControl（ACL 路径）");
        }
        if (!strippedSource.Contains("PipeOptions.CurrentUserOnly", StringComparison.Ordinal))
        {
            violations.Add("UiSingleInstance 缺少 PipeOptions.CurrentUserOnly（同用户边界未落实）");
        }
        return violations;
    }

    [Fact]
    public void UiSingleInstance_Should_Not_Call_The_Acl_Pipe_Api()
    {
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Services", "UiSingleInstance.cs");
        var violations = ClassifyUiSingleInstance(stripped);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void UiSingleInstance_Guard_Negative_Self_Proof()
    {
        var green = SourceLint.ReadStripped("src", "PhotoPrivacy.Ui", "Services", "UiSingleInstance.cs");
        var failing = new (string Source, string Reason)[]
        {
            (green + Environment.NewLine + "var p = NamedPipeServerStreamAcl.Create(null);", "ACL 调用回潮"),
            (green.Replace("PipeOptions.CurrentUserOnly", "PipeOptions.Asynchronous"), "同用户边界被删"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green): " + reason);
                continue;
            }
            if (ClassifyUiSingleInstance(source).Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---------- 2. systemd unit 模板安全设置 ----------

    internal static List<string> ClassifySystemdUnit(string script)
    {
        var violations = new List<string>();
        if (!script.Contains("RuntimeDirectoryMode=0750", StringComparison.Ordinal))
        {
            violations.Add("systemd unit 模板缺少 RuntimeDirectoryMode=0750（组边界目录）");
        }
        if (!script.Contains("usermod -aG", StringComparison.Ordinal))
        {
            violations.Add("安装脚本缺少 GUI 用户组供给（usermod -aG）");
        }
        if (!script.Contains("--gui-user", StringComparison.Ordinal))
        {
            violations.Add("安装脚本缺少 --gui-user 入口（组供给无法指定对象）");
        }
        return violations;
    }

    [Fact]
    public void Systemd_Unit_Template_Should_Pin_RuntimeDirectoryMode_And_Group_Supply()
    {
        var violations = ClassifySystemdUnit(SourceLint.Read("scripts", "install-systemd-service.sh"));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Systemd_Unit_Template_Guard_Negative_Self_Proof()
    {
        var green = SourceLint.Read("scripts", "install-systemd-service.sh");
        var failing = new (string Source, string Reason)[]
        {
            (green.Replace("RuntimeDirectoryMode=0750", "RuntimeDirectoryMode=0755"), "目录模式被放宽"),
            (green.Replace("usermod -aG", "# removed"), "组供给被移除"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green): " + reason);
                continue;
            }
            if (ClassifySystemdUnit(source).Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---------- 3. FirstPipeInstance 仅首实例 ----------

    internal static List<string> ClassifyFirstInstanceOnly(string strippedTransportSource)
    {
        var violations = new List<string>();
        var firstInstanceCalls = CountOccurrences(strippedTransportSource, "CreatePipeServer(_pipeName, isFirstInstance: true)");
        var nextInstanceCalls = CountOccurrences(strippedTransportSource, "CreatePipeServer(_pipeName, isFirstInstance: false)");
        if (firstInstanceCalls != 1)
        {
            violations.Add("首实例调用点应恰为 1 处（ListenAsync 预建），实测 " + firstInstanceCalls);
        }
        if (nextInstanceCalls != 1)
        {
            violations.Add("后续实例调用点应恰为 1 处（accept 后预建），实测 " + nextInstanceCalls);
        }

        var flagIdx = strippedTransportSource.IndexOf("PipeOptions.FirstPipeInstance", StringComparison.Ordinal);
        if (flagIdx < 0)
        {
            violations.Add("FirstPipeInstance 缺失：首实例防抢占已失效");
            return violations;
        }

        var guardIdx = strippedTransportSource.LastIndexOf("if (isFirstInstance)", flagIdx, StringComparison.Ordinal);
        if (guardIdx < 0)
        {
            violations.Add("FirstPipeInstance 不再受 if (isFirstInstance) 守卫（标志会被施加到后续实例）");
        }

        return violations;
    }

    [Fact]
    public void NamedPipeTransport_Should_Apply_FirstPipeInstance_Only_To_The_First_Instance()
    {
        var stripped = SourceLint.ReadStripped("src", "PhotoPrivacy.Ipc", "NamedPipeIpcTransport.cs");
        var violations = ClassifyFirstInstanceOnly(stripped);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void NamedPipeTransport_First_Instance_Guard_Negative_Self_Proof()
    {
        var green = SourceLint.ReadStripped("src", "PhotoPrivacy.Ipc", "NamedPipeIpcTransport.cs");
        var failing = new (string Source, string Reason)[]
        {
            (green.Replace("CreatePipeServer(_pipeName, isFirstInstance: true)", "CreatePipeServer(_pipeName, isFirstInstance: false)"), "首实例调用点被改为后续实例"),
            (green.Replace("if (isFirstInstance)", "if (true)"), "FirstPipeInstance 守卫被短路"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green): " + reason);
                continue;
            }
            if (ClassifyFirstInstanceOnly(source).Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
