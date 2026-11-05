using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// （#187）服务端 UDS 安全缺省的行为测试：安装器把目录收紧到 0750、把 socket 访问组与数据组
/// 分开之后，代码侧的两条缺省路径仍能静默退化，安装器写得再严也拦不住：
/// <list type="number">
/// <item>
/// <b>缺口 1</b>：<c>ListenAsync</c> 裸 <c>Directory.CreateDirectory(dir)</c> 按 umask 建 0755，
/// 目录可被任意本地用户遍历（端点存在性/目录内容外泄）；靠「目录 0750 收口」的模型里等于一层防线被缺省值替换。
/// </item>
/// <item>
/// <b>缺口 2</b>：<c>PHOTOPRIVACY_SERVICE_GROUP</c> 缺失时回落 <c>photoprivacy</c>——正好是数据组名，
/// 手起 Worker / unit 未更新 / 容器内运行都拿不到注入，组隔离静默退化成 CWE-732 原始形态。
/// </item>
/// </list>
/// 主体断言真实行为（真实建目录、真实读文件模式、真实解析环境变量）：缺省值本身既是行为也是契约，
/// 改回旧形态必红；末尾另留一道源码结构守卫（<see cref="ClassifySecurityDefaults"/>），
/// 覆盖本机 umask / 注入路径恰好掩盖回潮的场景（developer 机器 umask 077 时 0755 行为用例可能假绿）。
/// 程序集级 <c>DisableTestParallelization</c>（TestAssemblyConfig）保证环境变量注入不串扰其他用例。
/// </summary>
public sealed class UdsSecurityDefaultsTests
{
    private const UnixFileMode ExpectedDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

    /// <summary>待建 socket 目录路径（父目录已存在、<c>sockets/</c> 一层尚不存在——正是缺目录的分支）。</summary>
    private static string NewMissingDirectorySocketPath()
    {
        var parent = Path.Combine(Path.GetTempPath(), "pp-ipc-187-" + Guid.NewGuid().ToString("N"));
        var sockets = Path.Combine(parent, "sockets");
        Directory.CreateDirectory(parent);
        return Path.Combine(sockets, "worker.sock");
    }

    private static void Cleanup(string socketPath)
    {
        // 目录已被传输层建成 0750（属主可进）：递归删干净，别留 CI 临时目录。
        var dir = Path.GetDirectoryName(socketPath);
        if (dir is not null && Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
        if (dir is not null)
        {
            var parent = Path.GetDirectoryName(dir);
            if (parent is not null && Directory.Exists(parent))
            {
                try { Directory.Delete(parent, recursive: true); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>固定 uid 的伪造凭据策略：让白名单/拒绝分支可被判据而不是判本机身份。</summary>
    private sealed class FixedUidPolicy : UnixPeerCredentialPolicy
    {
        private readonly uint? _ownUid;
        private readonly IReadOnlySet<uint> _groupMembers;

        /// <summary>构造固定凭据策略。</summary>
        /// <param name="ownUid">伪造的本进程 uid（null 模拟读不到）。</param>
        /// <param name="groupMemberUids">伪造的组名 → 成员 uid 集合。</param>
        public FixedUidPolicy(uint? ownUid, IReadOnlySet<uint>? groupMemberUids = null)
        {
            _ownUid = ownUid;
            _groupMembers = groupMemberUids ?? new HashSet<uint>();
        }

        public override uint? ReadOwnUid() => _ownUid;

        public override uint? ReadPeerUid(System.Net.Sockets.Socket socket) => 9999;

        public override IReadOnlySet<uint> ReadGroupMemberUids(string groupName) => _groupMembers;
    }

    /// <summary>在给定 PHOTOPRIVACY_SERVICE_GROUP（含 null/空白）下执行同步动作，退出时恢复原值。</summary>
    /// <param name="value">本次注入值。</param>
    /// <param name="action">被测动作。</param>
    private static void WithServiceGroupEnv(string? value, Action action)
    {
        var name = UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable;
        var original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    /// <summary>异步版 <see cref="WithServiceGroupEnv"/>：await 期间环境变量保持注入，结束后恢复。</summary>
    /// <param name="value">本次注入值。</param>
    /// <param name="action">被测异步动作。</param>
    private static async Task WithServiceGroupEnvAsync(string? value, Func<Task> action)
    {
        var name = UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable;
        var original = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, value);
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }

    // ------------------------------------------------------------------
    // 缺口 1：socket 目录的分支二 —— 目录不存在时建成 0750（不落 umask 的 0755）
    // ------------------------------------------------------------------

    [Fact]
    public async Task Listen_Should_Create_Missing_Socket_Directory_As_0750_Not_Umask_0755()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewMissingDirectorySocketPath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            Assert.False(Directory.Exists(dir), "前提：用例必须从「目录不存在」的分支起步");

            await using var transport = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly);
            await transport.ListenAsync(CancellationToken.None);

            // 回潮形态：裸 CreateDirectory 按 umask（CI 常见 022）建成 0755——组与其他人可遍历。
            Assert.Equal(ExpectedDirectoryMode, File.GetUnixFileMode(dir));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task Listen_Should_Create_Missing_Socket_Directory_As_0750_In_Service_Mode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewMissingDirectorySocketPath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            await WithServiceGroupEnvAsync(UnixDomainSocketIpcTransport.DefaultServiceGroupName, async () =>
            {
                await using var transport = new UnixDomainSocketIpcTransport(
                    path, UnixSocketAccessMode.ServiceShared, peerCredentialPolicy: new FixedUidPolicy(1000));
                await transport.ListenAsync(CancellationToken.None);

                Assert.Equal(ExpectedDirectoryMode, File.GetUnixFileMode(dir));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite,
                    File.GetUnixFileMode(path));
            });
        }
        finally
        {
            Cleanup(path);
        }
    }

    // ------------------------------------------------------------------
    // 缺口 1：socket 目录的分支一 —— 目录已存在时**不改**权限（安装器/服务管理器是事实源）
    // ------------------------------------------------------------------

    [Fact]
    public async Task Listen_Should_Not_Touch_Mode_Of_An_Existing_Socket_Directory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewMissingDirectorySocketPath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            // 安装器建好的目录：0750，属主取自安装动作（这里并不同属主，只管模式不被覆写）。
            Directory.CreateDirectory(dir);
            File.SetUnixFileMode(dir, ExpectedDirectoryMode);

            await using var transport = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly);
            await transport.ListenAsync(CancellationToken.None);

            // 回潮形态：无条件 chmod 会把安装器/运维收紧的目录（例如 0700 的取证目录）放宽。
            Assert.Equal(ExpectedDirectoryMode, File.GetUnixFileMode(dir));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task Listen_Should_Preserve_A_Tighter_Existing_Socket_Directory_Mode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = NewMissingDirectorySocketPath();
        var dir = Path.GetDirectoryName(path)!;
        try
        {
            // 比 0750 更紧的既存目录（0700）：兜底路径不得把它放宽到 0750——
            // 「不改既存权限」必须对两侧都成立，否则等于悄悄扩大权限面。
            Directory.CreateDirectory(dir);
            var stricter = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            File.SetUnixFileMode(dir, stricter);

            await using var transport = new UnixDomainSocketIpcTransport(
                path, UnixSocketAccessMode.BackgroundOnly);
            await transport.ListenAsync(CancellationToken.None);

            Assert.Equal(stricter, File.GetUnixFileMode(dir));
        }
        finally
        {
            Cleanup(path);
        }
    }

    // ------------------------------------------------------------------
    // 缺口 2：组名缺失即 fail-closed，且诊断点明环境变量名
    // ------------------------------------------------------------------

    [Fact]
    public void ResolveServiceGroupName_Should_Fail_Closed_Without_Environment_Variable()
    {
        WithServiceGroupEnv(null, () =>
        {
            var ex = Assert.Throws<InvalidOperationException>(
                UnixDomainSocketIpcTransport.ResolveServiceGroupName);
            // 诊断必须点明变量名——否则「缺注入」在安装现场只剩一句莫名异常。
            Assert.Contains(
                UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable,
                ex.Message,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ResolveServiceGroupName_Should_Fail_Closed_For_Whitespace_Environment_Variable()
    {
        WithServiceGroupEnv("   ", () =>
        {
            var ex = Assert.Throws<InvalidOperationException>(
                UnixDomainSocketIpcTransport.ResolveServiceGroupName);
            Assert.Contains(
                UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable,
                ex.Message,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Service_Mode_Transport_Should_Refuse_To_Construct_Without_Group_Injection()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        WithServiceGroupEnv(null, () =>
        {
            // 缺口 2 的回潮形态：缺注入回落数据组，照样构造成功、照样按 0660 绑定，
            // 「组隔离」只剩一层同组可写的空壳。必须构造期即拒。
            var ex = Assert.Throws<InvalidOperationException>(() =>
                new UnixDomainSocketIpcTransport(
                    Path.Combine(Path.GetTempPath(), "pp-ipc-187-missing-group.sock"),
                    UnixSocketAccessMode.ServiceShared));
            Assert.Contains(
                UnixDomainSocketIpcTransport.ServiceGroupEnvironmentVariable,
                ex.Message,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Default_Service_Group_Should_Not_Be_The_Data_Group()
    {
        // 数据组名（install-*.sh 的 GROUP_NAME 默认值）与 socket 访问组必须不同名：
        // 缺省值一旦落在数据组上，组成员就同时拿到 hot/quarantine 的 2770 组写。
        Assert.NotEqual("photoprivacy", UnixDomainSocketIpcTransport.DefaultServiceGroupName);
        Assert.Equal("photoprivacy-ipc", UnixDomainSocketIpcTransport.DefaultServiceGroupName);
    }

    [Fact]
    public void ResolveServiceGroupName_Should_Trim_Injected_Name()
    {
        // plist/systemd 注入值带空白（手工拼 unit、沿 shell 变量带尾空格）时按实际组名解析，
        // 不把空白当「已注入」放行一个不存在的组名（比 fail-closed 更差：白名单空集、合法调用者全拒）。
        WithServiceGroupEnv("  photoprivacy-ipc-custom  ", () =>
            Assert.Equal("photoprivacy-ipc-custom", UnixDomainSocketIpcTransport.ResolveServiceGroupName()));
    }

    [Fact]
    public async Task Background_Mode_Should_Not_Require_Group_Injection()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await WithServiceGroupEnvAsync(null, async () =>
        {
            // 后台模式 socket 0600 仅属主可连，不看组：手工起后台 Worker、容器内运行缺注入不是错误。
            // 回潮形态：把 fail-closed 一刀切到全部模式，后台端点在手起场景无故拒启。
            var path = NewMissingDirectorySocketPath();
            try
            {
                await using var transport = new UnixDomainSocketIpcTransport(
                    path, UnixSocketAccessMode.BackgroundOnly);
                Assert.Equal(UnixSocketAccessMode.BackgroundOnly, transport.AccessMode);
                await transport.ListenAsync(CancellationToken.None);
            }
            finally
            {
                Cleanup(path);
            }
        });
    }

    // ------------------------------------------------------------------
    // 回潮守卫：把 #187 的两条缺省形态钉在源码结构上（行为用例之外的第二道）。
    // ------------------------------------------------------------------

    /// <summary>
    /// 对 UnixDomainSocketIpcTransport 源码（已剥注释）分类 #187 的两条回潮形态：
    /// ① 组名解析出现「缺失即回落」分支（<c>? DefaultServiceGroupName :</c> 落在解析函数里），
    ///    或解析函数不再抛异常；② 目录创建退回不带模式的裸 <c>Directory.CreateDirectory(dir)</c>。
    /// 返回违规原因，空表示绿。
    /// </summary>
    internal static List<string> ClassifySecurityDefaults(string transportSource)
    {
        var violations = new List<string>();

        var resolveIdx = transportSource.IndexOf("public static string ResolveServiceGroupName()", StringComparison.Ordinal);
        if (resolveIdx < 0)
        {
            violations.Add("ResolveServiceGroupName 不在（组名解析入口被移走，fail-closed 无从核对）");
        }
        else
        {
            var body = ExtractBalancedBlock(transportSource, transportSource.IndexOf('{', resolveIdx));
            if (!body.Contains("throw new InvalidOperationException", StringComparison.Ordinal))
            {
                // 缺口 2 原形态：缺注入回落到 DefaultServiceGroupName（旧值 = 数据组名），继续跑。
                violations.Add("ResolveServiceGroupName 缺注入不再 fail-closed（回落某个组名继续跑）");
            }
            if (body.Contains("DefaultServiceGroupName", StringComparison.Ordinal))
            {
                violations.Add("ResolveServiceGroupName 仍引用缺省组名（缺注入 → 回落数据组的旧形态）");
            }
        }

        // 缺口 1 原形态：裸 CreateDirectory(dir)（单参）按 umask 建 0755。
        // 用正则而非字面量：变量名重命名不该把守卫打成假红，判的是「有没有不带模式的创建」。
        var bareCreate = System.Text.RegularExpressions.Regex.IsMatch(
            transportSource,
            @"Directory\s*\.\s*CreateDirectory\s*\(\s*(?!path\s*,)[^,()]+\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromSeconds(5));
        if (bareCreate)
        {
            violations.Add("socket 目录退回不带模式的 Directory.CreateDirectory(...)（umask 0755，绕开安装器 0750）");
        }

        var createsWithMode = System.Text.RegularExpressions.Regex.IsMatch(
            transportSource,
            @"Directory\s*\.\s*CreateDirectory\s*\(\s*\w+\s*,\s*SocketDirectoryMode\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromSeconds(5));
        if (!createsWithMode)
        {
            violations.Add("socket 目录兜底创建不再显式施加 SocketDirectoryMode（0750）");
        }

        // 目录从出现起就要是 0750：创建后必须显式 SetUnixFileMode（createMode 受 umask 影响）。
        var chmodAfterCreate = System.Text.RegularExpressions.Regex.IsMatch(
            transportSource,
            @"File\s*\.\s*SetUnixFileMode\s*\(\s*\w+\s*,\s*SocketDirectoryMode\s*\)",
            System.Text.RegularExpressions.RegexOptions.Compiled,
            TimeSpan.FromSeconds(5));
        if (!chmodAfterCreate)
        {
            violations.Add("socket 目录创建后不再显式 SetUnixFileMode(SocketDirectoryMode)（umask 可能放宽）");
        }

        return violations;
    }

    /// <summary>从指定开括号位置截取平衡的 {…} 块（守卫分类器用）。</summary>
    /// <param name="text">源码文本。</param>
    /// <param name="openBraceIndex">开括号下标。</param>
    /// <returns>含首尾括号的块文本。</returns>
    /// <exception cref="InvalidOperationException">括号不配对。</exception>
    private static string ExtractBalancedBlock(string text, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[openBraceIndex..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException("unbalanced braces in transport source");
    }

    /// <summary>
    /// 防的具体回潮形态（issue #187）：① <c>ResolveServiceGroupName</c> 改回「缺失即回落
    /// <c>DefaultServiceGroupName</c>」——旧缺省值就是数据组名，组隔离静默退化成 CWE-732 原始形态；
    /// ② <c>ListenAsync</c> 的目录兜底改回裸 <c>Directory.CreateDirectory(dir)</c>，按 umask 建 0755。
    /// 两条都是「行为用例在 ubuntu CI 上能绿、真实安装机上却是漏洞」的形态：行为用例判的是本机 umask 与
    /// 注入路径，源码结构判的是这两条缺省分支还在不在，故各留一道。
    /// 关联 issue #187。
    /// </summary>
    [Fact]
    public void Defaults_Guard_Should_Flag_Regressed_Transport_Source()
    {
        var green = SourceLint.ReadStrippedPreservingLines(
            "src", "PhotoPrivacy.Ipc", "UnixDomainSocketIpcTransport.cs");
        var baseline = ClassifySecurityDefaults(green);
        Assert.True(baseline.Count == 0, "基线不绿：" + string.Join("; ", baseline));

        var failing = new (string Source, string Reason)[]
        {
            (
                green.Replace(
                    "throw new InvalidOperationException(ServiceGroupDiagnostic);",
                    "return DefaultServiceGroupName;"),
                "组名解析回落到缺省组名"),
            (
                green.Replace(
                    "Directory.CreateDirectory(level, SocketDirectoryMode);",
                    "Directory.CreateDirectory(dir);"),
                "目录兜底退回裸创建"),
            (
                green.Replace("File.SetUnixFileMode(level, SocketDirectoryMode);", "// removed"),
                "创建后不再收紧目录模式"),
        };

        var failures = new List<string>();
        foreach (var (source, reason) in failing)
        {
            if (source == green)
            {
                failures.Add("MUTATION DID NOT APPLY (sample equals green source): " + reason);
                continue;
            }

            if (ClassifySecurityDefaults(source).Count == 0)
            {
                failures.Add("MISSED (should be red): " + reason);
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
