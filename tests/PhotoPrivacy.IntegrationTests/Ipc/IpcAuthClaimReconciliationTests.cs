namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 票 04 对账守卫（不变量⑤宣称-实现一致）+ 失效即红预演。
/// 锁定三处「承诺 ↔ 实现」对应关系：
/// 1) AGENTS.md §5 IPC 规则第 1 条 = 认证依赖 OS 身份边界（ADR 0067 口径）；
/// 2) UiSingleInstance 不再调用 NamedPipeServerStreamAcl（AGENTS.md §5 第 5 条禁令，
///    修复前该文件实际在用 ACL —— 典型宣称-实现脱节）；
/// 3) install-systemd-service.sh 的 unit 模板钉住 RuntimeDirectoryMode=0750 与 GUI 用户组供给；
/// 4) NamedPipeIpcTransport 的 FirstPipeInstance 仅施加于 ListenAsync 预建的首实例
///    （accept 后预建的后续实例不得加标志 —— 票面明列的不可破坏结构约束）。
/// 均为去注释后的源码文本断言（注释剥离避免「仅文档提及」造成假阳性）。
/// </summary>
public sealed class IpcAuthClaimReconciliationTests
{
    private const string AgentsClaimAnchor = "同 OS 用户进程不在防御范围内";
    private const string RetiredClaim = "1. **命名管道需认证** — 验证连接方身份";

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

    // ---------- 1. AGENTS.md 承诺行对账 ----------

    internal static List<string> ClassifyAgentsMdClaim(string agentsMd)
    {
        var violations = new List<string>();
        if (!agentsMd.Contains(AgentsClaimAnchor, StringComparison.Ordinal))
        {
            violations.Add("AGENTS.md 缺少 ADR 0067 认证口径（同 OS 用户进程不在防御范围内）");
        }
        if (!agentsMd.Contains("PipeOptions.CurrentUserOnly", StringComparison.Ordinal))
        {
            violations.Add("AGENTS.md 未登记 Windows 主机制 PipeOptions.CurrentUserOnly");
        }
        if (agentsMd.Contains(RetiredClaim, StringComparison.Ordinal))
        {
            violations.Add("AGENTS.md 仍保留未兑现的旧承诺：命名管道需认证 — 验证连接方身份");
        }
        return violations;
    }

    [Fact]
    public void AgentsMd_Ipc_Auth_Claim_Should_Match_Implementation_Scope()
    {
        var violations = ClassifyAgentsMdClaim(SourceLint.Read("AGENTS.md"));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void AgentsMd_Ipc_Auth_Claim_Guard_Negative_Self_Proof()
    {
        var green = SourceLint.Read("AGENTS.md");
        var failing = new (string Source, string Reason)[]
        {
            (green.Replace(AgentsClaimAnchor, "（口径被删）"), "认证口径被删"),
            (green.Replace("PipeOptions.CurrentUserOnly", "SomeOtherOption"), "主机制登记被删"),
            (green + Environment.NewLine + RetiredClaim, "旧未兑现承诺回潮"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green): " + reason);
                continue;
            }
            if (ClassifyAgentsMdClaim(source).Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---------- 2. UiSingleInstance 不再用 ACL API ----------

    internal static List<string> ClassifyUiSingleInstance(string strippedSource)
    {
        var violations = new List<string>();
        if (strippedSource.Contains("NamedPipeServerStreamAcl", StringComparison.Ordinal))
        {
            violations.Add("UiSingleInstance 重新调用了 NamedPipeServerStreamAcl（ADR 0035 已证泄漏，且违反 AGENTS.md §5 第 5 条）");
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

    // ---------- 3. systemd unit 模板安全设置 ----------

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

    // ---------- 4. FirstPipeInstance 仅首实例（不可破坏的结构约束） ----------

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
