using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// IPC 与部署安全不变量守卫：每条断言都直接对账代码/脚本/配置，改措辞不会让它变红。
/// 1) install-systemd-service.sh 的 unit 模板必须钉住 RuntimeDirectoryMode=0750 并提供 GUI 用户组供给；
/// 2) NamedPipeIpcTransport 的 FirstPipeInstance 仅施加于 listen 期首例（#7 拓扑：后台直建/
///    服务随机名建例/通告失败回退静态名重绑，恰 3 处；accept 后预建的后续实例不得携带）；
/// 3) 服务管道通告名登记键的 Rust 读侧与 C# 写侧字面量对账（#7 命名熵的发现通道主缝）。
/// 每组守卫都带 negative self-proof：把源码改坏后备守卫必须变红，防止守卫空转。
/// 原 UiSingleInstance ACL 守卫随旧 Ui 工程退役（#61）；新 UI 单实例管线的 DACL 决策见
/// ui/crates/photoprivacy-ipc/src/single_instance.rs（Design(#49)），由 cargo test 守护。
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

    // ---------- 1. systemd unit 模板安全设置 ----------

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

    // ---------- 1b. launchd 组边界：socket 访问组必须独立于数据组（#167 评审 CWE-732） ----------

    internal static List<string> ClassifyLaunchdGroupBoundary(string script, string socketEnvName)
    {
        var violations = new List<string>();
        if (!script.Contains("--socket-group", StringComparison.Ordinal))
        {
            violations.Add("launchd 安装脚本缺少独立的 --socket-group（GUI 用户只能退回数据组）");
        }
        if (script.Contains("-a \"$GUI_USER\" -t user \"$GROUP_NAME\"", StringComparison.Ordinal))
        {
            violations.Add("launchd 安装脚本仍把 GUI 用户加进数据组（2770 组写可绕过 IPC 删审计/隔离）");
        }
        if (!script.Contains("-a \"$GUI_USER\" -t user \"$SOCKET_GROUP\"", StringComparison.Ordinal))
        {
            violations.Add("launchd 安装脚本未把 GUI 用户加进 socket 访问组");
        }
        if (!script.Contains("<string>${SOCKET_GROUP}</string>", StringComparison.Ordinal))
        {
            violations.Add("launchd plist 的 GroupName 未指向 socket 访问组");
        }
        if (!script.Contains(socketEnvName, StringComparison.Ordinal))
        {
            violations.Add("launchd plist 未注入 " + socketEnvName + "（服务端白名单与 socket 组脱节）");
        }
        if (!script.Contains("--gui-user", StringComparison.Ordinal))
        {
            violations.Add("launchd 安装脚本缺少 --gui-user 入口（osascript 提权链路拿不到 SUDO_USER）");
        }
        return violations;
    }

    [Fact]
    public void Launchd_Installer_Should_Separate_Socket_Group_From_Data_Group()
    {
        var violations = ClassifyLaunchdGroupBoundary(
            SourceLint.Read("scripts", "install-launchd-service.sh"),
            UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Launchd_Group_Boundary_Guard_Negative_Self_Proof()
    {
        var green = SourceLint.Read("scripts", "install-launchd-service.sh");
        var envName = UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable;
        var failing = new (string Source, string Reason)[]
        {
            (
                green.Replace(
                    "-a \"$GUI_USER\" -t user \"$SOCKET_GROUP\"",
                    "-a \"$GUI_USER\" -t user \"$GROUP_NAME\""),
                "GUI 用户被加进数据组（组写回潮）"),
            (green.Replace("--gui-user", "--user"), "GUI 用户入口被移除"),
            (green.Replace("<string>${SOCKET_GROUP}</string>", "<string>${GROUP_NAME}</string>"), "plist GroupName 退回数据组"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green): " + reason);
                continue;
            }
            if (ClassifyLaunchdGroupBoundary(source, envName).Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // ---------- 2. FirstPipeInstance 仅 listen 期首例 ----------

    internal static List<string> ClassifyFirstInstanceOnly(string strippedTransportSource)
    {
        var violations = new List<string>();
        // #7 后的首例建例点恰 3 处：ListenAsync 后台分支直建、服务模式随机名建例、
        // 通告失败回退静态名重绑——三处都是 listen 期首例（语义等价于拆分前的单一调用点）；
        // 后续实例（accept 后预建）恰 1 处且不得携带 FirstPipeInstance。
        var firstInstanceCalls = CountOccurrences(strippedTransportSource, "isFirstInstance: true");
        var nextInstanceCalls = CountOccurrences(strippedTransportSource, "isFirstInstance: false");
        if (firstInstanceCalls != 3)
        {
            violations.Add("首例建例点应恰为 3 处（后台直建/服务随机名/通告失败回退静态名，#7 拓扑），实测 " + firstInstanceCalls);
        }
        if (nextInstanceCalls != 1)
        {
            violations.Add("后续实例建例点应恰为 1 处（accept 后预建），实测 " + nextInstanceCalls);
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
            (green.Replace("isFirstInstance: true", "isFirstInstance: false"), "首例建例点被改为后续实例"),
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

    // ---------- 3. 服务管道通告名登记键的 Rust/C# 两侧对账（#7） ----------

    /// <summary>needle 构造与断言同一处，供正向断言与 negative self-proof 共用（本类惯例）。</summary>
    internal static List<string> ClassifyServicePipeAdvertisementRegistryNames(
        string strippedRustSource, string subkey, string valueName)
    {
        var violations = new List<string>();
        // Rust 源码里反斜杠以 \\ 转义形态出现：C# 侧常量按同规则转义后比对。
        var subkeyLiteral = subkey.Replace(@"\", @"\\");
        if (!strippedRustSource.Contains($"wide(\"{subkeyLiteral}\")", StringComparison.Ordinal))
        {
            violations.Add($"Rust 读侧缺少通告键字面量 wide(\"{subkeyLiteral}\")（endpoints.rs）");
        }
        if (!strippedRustSource.Contains($"wide(\"{valueName}\")", StringComparison.Ordinal))
        {
            violations.Add($"Rust 读侧缺少通告值名字面量 wide(\"{valueName}\")（endpoints.rs）");
        }
        return violations;
    }

    [Fact]
    public void Service_Pipe_Advertisement_Registry_Names_Match_Rust_Client_Literals()
    {
        // 防（#7 审查返修 m-R1）：命名熵闭环押在 C# 写侧（WindowsServicePipeAcl）与 Rust 读侧
        // （endpoints.rs 的 read_advertised_service_pipe_name）的注册表键/值名一致上——漂移形态是
        // UI 静默读不到通告值、永久退回静态名（熵关闭，回到 #7 前攻击面），且无行为测试会红
        // （Rust 无法引用 C# 常量）。按本类的 source-lint 对账先例钉住两侧字面量；安装脚本侧的
        // 同类重复自 #70 起即无守卫（既有先例，卸载清三值清单已在 ipc-contract 登记），不扩本票面。
        var rust = SourceLint.ReadStripped("ui", "crates", "photoprivacy-ipc", "src", "endpoints.rs");
        var violations = ClassifyServicePipeAdvertisementRegistryNames(
            rust, WindowsServicePipeAcl.RegistrySubKeyName, WindowsServicePipeAcl.AdvertisedServicePipeValueName);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Service_Pipe_Advertisement_Registry_Names_Guard_Negative_Self_Proof()
    {
        // 失效即红自证（终审 M-F1，本类惯例）：改坏 Rust 源码的键/值名字面量后守卫必须变红，
        // 防止对账断言随字面量漂移而空转。verbatim 字面量保持与 Rust 源码一致的双反斜杠字节。
        var green = SourceLint.ReadStripped("ui", "crates", "photoprivacy-ipc", "src", "endpoints.rs");
        var failing = new (string Source, string Reason)[]
        {
            (green.Replace(@"SOFTWARE\\PhotoPrivacy", @"SOFTWARE\\PhotoPrivacyX"), "通告键被改写"),
            (green.Replace("ServicePipeName", "ServicePipeNameX"), "通告值名被改写"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green): " + reason);
                continue;
            }
            if (ClassifyServicePipeAdvertisementRegistryNames(
                    source, WindowsServicePipeAcl.RegistrySubKeyName, WindowsServicePipeAcl.AdvertisedServicePipeValueName)
                .Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
