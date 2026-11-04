namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （#34 兼容性复测收口）：.NET 10 升级兼容配置单一来源守卫——钉住
/// net10.0 目标框架、Workstation GC（gcServer=false / gcConcurrent=true）、
/// PublishReadyToRun=true 与 Serilog 各包固定版本不回归。
///
/// 背景：.NET 10 升级（提交 693b2642 等）涉及 GC 调优（Workstation GC +
/// ReadyToRun 预编译，e5de5fef）与 Serilog 4.x 系列。这些配置一旦漂移
/// （目标框架降级、GC 模式回退、Serilog 版本浮动），会在用户侧（#34 用户侧
/// 复测）暴露兼容性问题。本守卫在 CI 静态锁定这些配置，防「升级后配置回潮」。
/// </summary>
public sealed class Net10CompatibilityGuardTests
{
    /// <summary>Workstation GC 的 runtimeconfig 模板路径（Worker 侧）。</summary>
    private const string RuntimeConfigTemplatePath = "src/PhotoPrivacy.Worker/runtimeconfig.template.json";

    /// <summary>Directory.Build.props（目标框架 + PublishReadyToRun 单一来源）。</summary>
    private const string DirectoryBuildPropsPath = "Directory.Build.props";

    /// <summary>Serilog 各包固定版本（.NET 10 兼容性复测确认的版本）。</summary>
    private static readonly (string Package, string Version)[] SerilogPackages =
    [
        ("Serilog", "4.3.0"),
        ("Serilog.Extensions.Hosting", "8.0.0"),
        ("Serilog.Settings.Configuration", "8.0.4"),
        ("Serilog.Sinks.Async", "2.0.0"),
        ("Serilog.Sinks.Console", "6.0.0"),
        ("Serilog.Sinks.File", "6.0.0"),
    ];

    /// <summary>各工程应持有的 Serilog 包（Core 只引 Serilog 本体，Worker 引全家桶）。</summary>
    private static readonly (string Project, string Label, string[] Packages)[] SerilogProjectMap =
    [
        ("PhotoPrivacy.Core", "PhotoPrivacy.Core.csproj", ["Serilog"]),
        ("PhotoPrivacy.Worker", "PhotoPrivacy.Worker.csproj",
            ["Serilog", "Serilog.Extensions.Hosting", "Serilog.Settings.Configuration",
             "Serilog.Sinks.Async", "Serilog.Sinks.Console", "Serilog.Sinks.File"]),
    ];

    internal static List<string> ClassifyRuntimeConfig(string content, string label)
    {
        // 防的具体回潮形态：GC 调优（e5de5fef）被回退——Workstation GC 的
        // gcServer=false 与 gcConcurrent=true 是托盘应用行业标准，回退到 Server GC
        // 会带来 10-20MB 固定开销（#34 用户侧复测关注点）。
        var violations = new List<string>();
        if (!content.Contains("\"gcServer\": false", StringComparison.Ordinal))
        {
            violations.Add(label + ": 缺少 gcServer=false（Workstation GC 回退）");
        }

        if (!content.Contains("\"gcConcurrent\": true", StringComparison.Ordinal))
        {
            violations.Add(label + ": 缺少 gcConcurrent=true（后台 GC 回退）");
        }

        return violations;
    }

    internal static List<string> ClassifyDirectoryBuildProps(string content, string label)
    {
        // 防的具体回潮形态：目标框架降级 / PublishReadyToRun 被摘除。
        // net10.0 是 .NET 10 升级的锚点；PublishReadyToRun=true 是 R2R 预编译
        // （e5de5fef M2），摘除会回退到 JIT 启动开销（#34 用户侧复测关注点）。
        var violations = new List<string>();
        if (!content.Contains("<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal))
        {
            violations.Add(label + ": 目标框架非 net10.0（.NET 10 升级锚点回退）");
        }

        if (!content.Contains("<PublishReadyToRun>true</PublishReadyToRun>", StringComparison.Ordinal))
        {
            violations.Add(label + ": 缺少 PublishReadyToRun=true（R2R 预编译回退）");
        }

        return violations;
    }

    internal static List<string> ClassifySerilogVersions(string content, string label, string[] packages)
    {
        // 防的具体回潮形态：Serilog 包版本浮动（升级/降级）破坏 .NET 10 兼容性。
        // 版本必须精确匹配复测确认的固定版本（防范围版本漂移）。
        var violations = new List<string>();
        foreach (var pkg in packages)
        {
            var version = SerilogPackages.First(p => p.Package == pkg).Version;
            var expected = $"<PackageReference Include=\"{pkg}\" Version=\"{version}\" />";
            if (!content.Contains(expected, StringComparison.Ordinal))
            {
                violations.Add(label + ": " + pkg + " 版本非固定 " + version);
            }
        }

        return violations;
    }

    [Fact]
    public void RuntimeConfig_Template_Should_Keep_Workstation_Gc()
    {
        // 防的具体回潮形态：runtimeconfig.template.json 被改成 Server GC 或掉 gcConcurrent，
        // 托盘应用固定开销回升（e5de5fef 的调优被回退）。关联 issue #34。
        var content = SourceLint.Read("src", "PhotoPrivacy.Worker", "runtimeconfig.template.json");
        var violations = ClassifyRuntimeConfig(content, RuntimeConfigTemplatePath);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Directory_Build_Props_Should_Keep_Net10_And_ReadyToRun()
    {
        // 防的具体回潮形态：TargetFramework 降级离开 net10.0、或 PublishReadyToRun 被摘除，
        // .NET 10 升级锚点与 R2R 启动收益丢失。关联 issue #34。
        var content = SourceLint.Read("Directory.Build.props");
        var violations = ClassifyDirectoryBuildProps(content, DirectoryBuildPropsPath);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Serilog_Packages_Should_Stay_On_Fixed_Net10_Compatible_Versions()
    {
        // 防的具体回潮形态：Serilog 依赖按工程映射校验——Core 只引 Serilog 本体，Worker 引全家桶，
        // 任一包漂离复测确认的固定版本即红；其余工程（Ipc 等）不引用 Serilog，不在扫描面内
        // （防「无引用工程误报」）。关联 issue #34。
        var violations = new List<string>();
        foreach (var (project, label, packages) in SerilogProjectMap)
        {
            violations.AddRange(ClassifySerilogVersions(
                SourceLint.Read("src", project, label), label, packages));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Net10_Compatibility_Guard_Negative_Self_Proof()
    {
        // 失效即红自证：GC 配置回退 / 目标框架降级 / R2R 摘除 / Serilog 版本浮动
        // 四类篡改必须报红，基线必须绿。关联 issue #34。
        var failures = new List<string>();

        // 基线必须绿。
        var cleanRuntime = SourceLint.Read("src", "PhotoPrivacy.Worker", "runtimeconfig.template.json");
        if (ClassifyRuntimeConfig(cleanRuntime, "runtimeconfig.template.json").Count != 0)
        {
            failures.Add("baseline runtimeconfig 误报");
        }

        var cleanProps = SourceLint.Read("Directory.Build.props");
        if (ClassifyDirectoryBuildProps(cleanProps, "Directory.Build.props").Count != 0)
        {
            failures.Add("baseline Directory.Build.props 误报");
        }

        // GC 回退篡改必须红。
        var gcDrift = cleanRuntime.Replace("\"gcServer\": false", "\"gcServer\": true", StringComparison.Ordinal);
        if (ClassifyRuntimeConfig(gcDrift, "runtimeconfig.template.json").Count == 0)
        {
            failures.Add("MISSED: gcServer 回退 true 未红");
        }

        var gcConcurrentDrift = cleanRuntime.Replace(
            "\"gcConcurrent\": true", "\"gcConcurrent\": false", StringComparison.Ordinal);
        if (ClassifyRuntimeConfig(gcConcurrentDrift, "runtimeconfig.template.json").Count == 0)
        {
            failures.Add("MISSED: gcConcurrent 回退 false 未红");
        }

        // 目标框架降级篡改必须红。
        var tfmDrift = cleanProps.Replace(
            "<TargetFramework>net10.0</TargetFramework>",
            "<TargetFramework>net9.0</TargetFramework>",
            StringComparison.Ordinal);
        if (ClassifyDirectoryBuildProps(tfmDrift, "Directory.Build.props").Count == 0)
        {
            failures.Add("MISSED: 目标框架降级 net9.0 未红");
        }

        // R2R 摘除篡改必须红。
        var r2rDrift = cleanProps.Replace(
            "<PublishReadyToRun>true</PublishReadyToRun>", string.Empty, StringComparison.Ordinal);
        if (ClassifyDirectoryBuildProps(r2rDrift, "Directory.Build.props").Count == 0)
        {
            failures.Add("MISSED: PublishReadyToRun 摘除未红");
        }

        // Serilog 版本浮动篡改必须红。
        var cleanSerilog = SourceLint.Read("src", "PhotoPrivacy.Worker", "PhotoPrivacy.Worker.csproj");
        var workerPackages = SerilogProjectMap.First(m => m.Project == "PhotoPrivacy.Worker").Packages;
        if (ClassifySerilogVersions(cleanSerilog, "PhotoPrivacy.Worker.csproj", workerPackages).Count != 0)
        {
            failures.Add("baseline Worker csproj Serilog 误报");
        }

        var serilogDrift = cleanSerilog.Replace(
            "<PackageReference Include=\"Serilog\" Version=\"4.3.0\" />",
            "<PackageReference Include=\"Serilog\" Version=\"4.2.0\" />",
            StringComparison.Ordinal);
        if (ClassifySerilogVersions(serilogDrift, "PhotoPrivacy.Worker.csproj", workerPackages).Count == 0)
        {
            failures.Add("MISSED: Serilog 版本浮动未红");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
