using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （#34 兼容性复测收尾）：.NET 10 升级兼容配置单一来源检查——钉住
/// net10.0 目标框架、Workstation GC（gcServer=false / gcConcurrent=true）、
/// PublishReadyToRun=true 与 Serilog 各包固定版本不回归。
///
/// 背景：.NET 10 升级（提交 693b2642 等）涉及 GC 调优（Workstation GC +
/// ReadyToRun 预编译，e5de5fef）与 Serilog 4.x 系列。这些配置一旦漂移
/// （目标框架降级、GC 模式回退、Serilog 版本浮动），会在用户侧（#34 用户侧
/// 复测）暴露兼容性问题。本检查在 CI 静态锁定这些配置，防「升级后配置老毛病再犯」。
///
/// #12 裁决：PublishReadyToRun=true 维持 props 中心化——与 AGENTS「发布属性只经脚本
/// 传入」的冲突按改规则定下来（其余发布属性只经发布脚本传入，ReadyToRun 是唯一例外，
/// AGENTS.md 发布条目已同步拆分），本检查的单一来源断言继续锁定该中心化。
///
/// 返修记录（PR #186 CodeRabbit 首轮 3 条 finding，逐项判定成立）：
/// - Major（props 文本匹配不查冲突定义与工程级覆盖）：改为 XML 解析取活跃元素，
///   重复定义即红（MSBuild 后定义者胜）；新增工程级文件（csproj / 子目录 props）
///   定义中心属性即红的覆盖检查（工程级定义优先于 Directory.Build.props 导入）。
/// - Minor（runtimeconfig 裸搜子串）：改为 JSON 解析取 runtimeOptions 的
///   gcServer/gcConcurrent 布尔有效值，缺键 / 非布尔 / 重复键 / 改值均红。
/// - Minor（Serilog Contains 不剥 XML 注释）：改为 XML 解析比对活跃
///   PackageReference，注释残留与重复引用不再骗绿。
/// 三条均为本检查漏报路径的收尾；轻量方案（XML/JSON 均在共享框架内，零新增依赖），
/// 不引入 MSBuild evaluation。
/// </summary>
public sealed class Net10CompatibilityGuardTests
{
    /// <summary>Workstation GC 的 runtimeconfig 模板路径（Worker 侧）。</summary>
    private const string RuntimeConfigTemplatePath = "src/PhotoPrivacy.Worker/runtimeconfig.template.json";

    /// <summary>Directory.Build.props（目标框架 + PublishReadyToRun 单一来源）。</summary>
    private const string DirectoryBuildPropsPath = "Directory.Build.props";

    /// <summary>工程级文件扫描根（src/ 与 tests/ 的 csproj 与子目录 props 都可能覆盖中心属性）。</summary>
    private static readonly string[] ProjectLevelScanRoots = ["src", "tests"];

    /// <summary>工程级文件扫描模式：csproj 本体 + 任何 props（子目录 Directory.Build.props 同罪）。</summary>
    private static readonly string[] ProjectLevelFilePatterns = ["*.csproj", "*.props"];

    /// <summary>中心单一来源属性——工程级（csproj / 子目录 props）定义会覆盖 Directory.Build.props，须整体禁绝。</summary>
    private static readonly string[] CentralSourcedProperties =
    [
        "TargetFramework",
        "PublishReadyToRun",
    ];

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
        // 要防的老毛病（PR #186 CodeRabbit Minor 返修）：GC 调优（e5de5fef）被回退——
        // gcServer=true（Server GC，托盘应用回升 10-20MB 固定开销）或 gcConcurrent=false；
        // 以及键丢失、值字符串化、重复键让有效值脱离判定。处置：JSON 解析按
        // runtimeOptions 的布尔有效值判定，不再裸搜子串。关联 issue #34。
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return [label + ": 非合法 JSON（模板损坏，MSBuild 合并同样会失败）"];
        }

        var violations = new List<string>();
        using (parsed)
        {
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return [label + ": 根节点非 JSON 对象（模板形态漂移）"];
            }

            if (!root.TryGetProperty("runtimeOptions", out var options) ||
                options.ValueKind != JsonValueKind.Object)
            {
                return [label + ": 缺少 runtimeOptions 对象（GC 配置载体没了）"];
            }

            violations.AddRange(ClassifyGcFlag(options, "gcServer", false, label));
            violations.AddRange(ClassifyGcFlag(options, "gcConcurrent", true, label));
        }

        return violations;
    }

    /// <summary>单枚 GC 开关的有效值判定：单一定义 + 布尔字面量 + 期望值，缺一即红。</summary>
    private static List<string> ClassifyGcFlag(JsonElement options, string name, bool expected, string label)
    {
        // 先数后取：不依赖 JsonDocument 对重复键的取值语义——重复即红，有效值脱离判定。
        var hits = 0;
        foreach (var property in options.EnumerateObject())
        {
            if (property.Name == name)
            {
                hits++;
            }
        }

        if (hits == 0)
        {
            return [label + ": 缺少 " + name + "=" + (expected ? "true" : "false") + "（Workstation GC 回退）"];
        }

        if (hits > 1)
        {
            return [label + ": " + name + " 重复定义 " + hits + " 处（多处定义互相覆盖，检查管不住最终生效值）"];
        }

        var value = options.GetProperty(name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return [label + ": " + name + " 非布尔字面量（字符串/数字形态，语义漂移）"];
        }

        if (value.GetBoolean() == expected)
        {
            return [];
        }

        return [label + ": " + name + "=" + value.GetBoolean() + "（预期 " + expected + "，Workstation GC 回退）"];
    }

    internal static List<string> ClassifyDirectoryBuildProps(string content, string label)
    {
        // 要防的老毛病（PR #186 CodeRabbit Major 返修）：目标框架降级 /
        // PublishReadyToRun 摘除或改值，以及 MSBuild 后定义者胜的冲突定义——同一属性
        // 第二处定义（哪怕值仍写 net10.0）让有效值脱离文本判定；XML 注释掉的旧行
        // 不再喂绿子串匹配。处置：XML 解析取活跃元素，单一定义 + 值精确匹配。
        // 关联 issue #34。
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException)
        {
            return [label + ": 非合法 XML（props 损坏，MSBuild 同样无法求值）"];
        }

        var violations = new List<string>();
        violations.AddRange(ClassifySingleSourcedProperty(doc, "TargetFramework", "net10.0", label));
        violations.AddRange(ClassifySingleSourcedProperty(doc, "PublishReadyToRun", "true", label));
        return violations;
    }

    /// <summary>中心属性在 props 内的单一来源判定：恰好一处活跃定义且值精确匹配。</summary>
    private static List<string> ClassifySingleSourcedProperty(XDocument doc, string name, string expected, string label)
    {
        var hits = doc.Descendants().Where(e => e.Name.LocalName == name).ToList();
        if (hits.Count == 0)
        {
            return [label + ": 缺少 <" + name + ">（.NET 10 升级必需的属性丢了）"];
        }

        if (hits.Count > 1)
        {
            return [label + ": <" + name + "> 活跃定义 " + hits.Count +
                    " 处——MSBuild 后定义的生效，检查管不住最终值（必须收敛回单一定义）"];
        }

        var value = hits[0].Value.Trim();
        if (value == expected)
        {
            return [];
        }

        return [label + ": <" + name + ">=" + value + "（预期 " + expected + "，升级必需的值被改回去）"];
    }

    internal static List<string> ClassifyProjectLevelOverrides(string content, string label)
    {
        // 要防的老毛病（PR #186 CodeRabbit Major 返修）：工程级文件（csproj 或子目录
        // Directory.Build.props——MSBuild 取最近一层）定义 TargetFramework /
        // PublishReadyToRun，工程级定义优先于根 Directory.Build.props 导入，中心检查
        // 照样绿而有效值已漂移。处置：src/ 与 tests/ 下全部工程级文件禁绝这两枚中心属性。
        // 关联 issue #34。
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException)
        {
            return [label + ": 非合法 XML（工程文件损坏）"];
        }

        var violations = new List<string>();
        foreach (var name in CentralSourcedProperties)
        {
            if (doc.Descendants().Any(e => e.Name.LocalName == name))
            {
                violations.Add(label + ": 工程级定义 <" + name +
                               "> 覆盖中心单一来源（Directory.Build.props 才是判定面，此定义须移除）");
            }
        }

        return violations;
    }

    internal static List<string> ClassifySerilogVersions(string content, string label, string[] packages)
    {
        // 要防的老毛病（PR #186 CodeRabbit Minor 返修）：Serilog 包版本浮动（升级/降级）
        // 破坏 .NET 10 兼容性；旧版本行被 XML 注释掉、活跃引用已改版本时，子串匹配仍骗绿。
        // 处置：XML 解析只看活跃 PackageReference，注释残留不计，重复活跃引用即红。
        // 关联 issue #34。
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException)
        {
            return [label + ": 非合法 XML（csproj 损坏，Serilog 版本检查失效）"];
        }

        var violations = new List<string>();
        foreach (var pkg in packages)
        {
            var version = SerilogPackages.First(p => p.Package == pkg).Version;
            var references = doc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference" &&
                            string.Equals((string?)e.Attribute("Include"), pkg, StringComparison.Ordinal))
                .ToList();

            if (references.Count == 0)
            {
                violations.Add(label + ": 缺少活跃引用 " + pkg + " " + version + "（丢失或整行进了注释）");
                continue;
            }

            if (references.Count > 1)
            {
                violations.Add(label + ": " + pkg + " 活跃引用 " + references.Count + " 处（重复引用）");
                continue;
            }

            var actual = (string?)references[0].Attribute("Version");
            if (!string.Equals(actual, version, StringComparison.Ordinal))
            {
                violations.Add(label + ": " + pkg + " 活跃版本 " + actual + " 非复测固定版本 " + version);
            }
        }

        return violations;
    }

    [Fact]
    public void RuntimeConfig_Template_Should_Keep_Workstation_Gc()
    {
        // 要防的老毛病：runtimeconfig.template.json 被改成 Server GC 或掉 gcConcurrent，
        // 托盘应用固定开销回升（e5de5fef 的调优被回退）。关联 issue #34。
        var content = SourceLint.Read("src", "PhotoPrivacy.Worker", "runtimeconfig.template.json");
        var violations = ClassifyRuntimeConfig(content, RuntimeConfigTemplatePath);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Directory_Build_Props_Should_Keep_Net10_And_ReadyToRun()
    {
        // 要防的老毛病：TargetFramework 降级离开 net10.0、PublishReadyToRun 被摘除/改值，
        // 或出现第二处定义让 MSBuild 后定义者胜（CodeRabbit Major 判定成立的漏报路径）。
        // 关联 issue #34。
        var content = SourceLint.Read("Directory.Build.props");
        var violations = ClassifyDirectoryBuildProps(content, DirectoryBuildPropsPath);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Project_Files_Should_Not_Override_Central_Framework_Or_ReadyToRun()
    {
        // 要防的老毛病：csproj / 子目录 props 里塞 <TargetFramework>net9.0</TargetFramework>
        // 或 <PublishReadyToRun>false</PublishReadyToRun>——工程级定义优先于根
        // Directory.Build.props 导入（MSBuild 取最近一层），中心检查照样绿而有效值已漂移
        // （CodeRabbit Major 判定成立的漏报路径）。关联 issue #34。
        var violations = new List<string>();
        foreach (var root in ProjectLevelScanRoots)
        {
            foreach (var pattern in ProjectLevelFilePatterns)
            {
                var files = SourceLint.EnumerateSourceFiles(root, pattern);
                if (pattern == "*.csproj")
                {
                    // csproj 必须存在（扫描面防半盲）；props 允许为零——根目录中心 props 不在扫描根下。
                    Assert.True(files.Count > 0, root + "/ 未找到任何 csproj——检查的扫描范围不对");
                }

                foreach (var file in files)
                {
                    var label = Path.GetRelativePath(SourceLint.RepoRoot, file).Replace('\\', '/');
                    violations.AddRange(ClassifyProjectLevelOverrides(File.ReadAllText(file), label));
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Serilog_Packages_Should_Stay_On_Fixed_Net10_Compatible_Versions()
    {
        // 要防的老毛病：Serilog 依赖按工程映射校验——Core 只引 Serilog 本体，Worker 引全家桶，
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
        // 失效即红自我验证（TEST-CONVENTIONS R2 配套）：GC 改值/缺键/重复键/字符串化、
        // 目标框架降级、R2R 摘除、props 冲突定义、props 注释化摘除、工程级覆盖、
        // Serilog 版本浮动/注释骗绿/重复引用——篡改必须报红，基线必须绿。关联 issue #34。
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

        // GC 键丢失 / 重复键 / 值字符串化篡改必须红（CodeRabbit Minor：子串匹配的漏报形态）。
        var gcMissing = cleanRuntime.Replace("\"gcServer\"", "\"gcServerGone\"", StringComparison.Ordinal);
        if (ClassifyRuntimeConfig(gcMissing, "runtimeconfig.template.json").Count == 0)
        {
            failures.Add("MISSED: gcServer 键丢失未红");
        }

        var gcDuplicate = cleanRuntime.Replace(
            "\"gcConcurrent\": true", "\"gcConcurrent\": true, \"gcConcurrent\": true", StringComparison.Ordinal);
        if (ClassifyRuntimeConfig(gcDuplicate, "runtimeconfig.template.json").Count == 0)
        {
            failures.Add("MISSED: gcConcurrent 重复键未红");
        }

        var gcStringified = cleanRuntime.Replace(
            "\"gcServer\": false", "\"gcServer\": \"false\"", StringComparison.Ordinal);
        if (ClassifyRuntimeConfig(gcStringified, "runtimeconfig.template.json").Count == 0)
        {
            failures.Add("MISSED: gcServer 字符串化未红");
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

        // props 内第二处定义（冲突定义，MSBuild 后定义者胜）必须红——该形态下原
        // net10.0 字符串仍在，子串匹配会骗绿（CodeRabbit Major 判定成立的漏报形态）。
        var conflictingProps = cleanProps.Replace(
            "</PropertyGroup>",
            "<TargetFramework>net9.0</TargetFramework></PropertyGroup>",
            StringComparison.Ordinal);
        if (ClassifyDirectoryBuildProps(conflictingProps, "Directory.Build.props").Count == 0)
        {
            failures.Add("MISSED: TargetFramework 冲突定义未红");
        }

        // props 属性整行注释化必须红（注释行不再喂绿子串匹配）。
        var commentMaskedProps = cleanProps.Replace(
            "<TargetFramework>net10.0</TargetFramework>",
            "<!-- <TargetFramework>net10.0</TargetFramework> -->",
            StringComparison.Ordinal);
        if (ClassifyDirectoryBuildProps(commentMaskedProps, "Directory.Build.props").Count == 0)
        {
            failures.Add("MISSED: TargetFramework 注释化摘除未红");
        }

        // 工程级文件覆盖必须红（CodeRabbit Major：工程定义优先于 Directory.Build.props）。
        const string cleanProjectSample = "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";
        if (ClassifyProjectLevelOverrides(cleanProjectSample, "clean-sample.csproj").Count != 0)
        {
            failures.Add("baseline 工程文件样本误报");
        }

        var projectOverride = cleanProjectSample.Replace(
            "</PropertyGroup>",
            "<TargetFramework>net9.0</TargetFramework><PublishReadyToRun>false</PublishReadyToRun></PropertyGroup>",
            StringComparison.Ordinal);
        if (ClassifyProjectLevelOverrides(projectOverride, "override-sample.csproj").Count == 0)
        {
            failures.Add("MISSED: 工程级覆盖中心属性未红");
        }

        // Serilog 基线必须绿。
        var cleanSerilog = SourceLint.Read("src", "PhotoPrivacy.Worker", "PhotoPrivacy.Worker.csproj");
        var workerPackages = SerilogProjectMap.First(m => m.Project == "PhotoPrivacy.Worker").Packages;
        if (ClassifySerilogVersions(cleanSerilog, "PhotoPrivacy.Worker.csproj", workerPackages).Count != 0)
        {
            failures.Add("baseline Worker csproj Serilog 误报");
        }

        // Serilog 版本浮动篡改必须红。
        var serilogDrift = cleanSerilog.Replace(
            "<PackageReference Include=\"Serilog\" Version=\"4.3.0\" />",
            "<PackageReference Include=\"Serilog\" Version=\"4.2.0\" />",
            StringComparison.Ordinal);
        if (ClassifySerilogVersions(serilogDrift, "PhotoPrivacy.Worker.csproj", workerPackages).Count == 0)
        {
            failures.Add("MISSED: Serilog 版本浮动未红");
        }

        // Serilog 注释骗绿篡改必须红（CodeRabbit Minor 判定成立的漏报形态）：旧行进注释、
        // 活跃行已改版本——子串匹配时钉住的字符串仍在注释里照样绿；XML 解析后必须红。
        var serilogCommentMasked = cleanSerilog.Replace(
            "<PackageReference Include=\"Serilog\" Version=\"4.3.0\" />",
            "<!-- <PackageReference Include=\"Serilog\" Version=\"4.3.0\" /> -->" + Environment.NewLine +
            "    <PackageReference Include=\"Serilog\" Version=\"4.2.0\" />",
            StringComparison.Ordinal);
        if (ClassifySerilogVersions(serilogCommentMasked, "PhotoPrivacy.Worker.csproj", workerPackages).Count == 0)
        {
            failures.Add("MISSED: Serilog 注释骗绿未红");
        }

        // Serilog 重复活跃引用必须红。
        var serilogDuplicate = cleanSerilog.Replace(
            "<PackageReference Include=\"Serilog\" Version=\"4.3.0\" />",
            "<PackageReference Include=\"Serilog\" Version=\"4.3.0\" />" + Environment.NewLine +
            "    <PackageReference Include=\"Serilog\" Version=\"4.2.0\" />",
            StringComparison.Ordinal);
        if (ClassifySerilogVersions(serilogDuplicate, "PhotoPrivacy.Worker.csproj", workerPackages).Count == 0)
        {
            failures.Add("MISSED: Serilog 重复活跃引用未红");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
