using System.Xml;
using System.Xml.Linq;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （correctness-round 顺带发现并收尾；#61 切换日核对；#12 检查缺口收尾）：
/// 发布属性单一来源检查——生产工程文件（csproj / props / targets）不得定义 SelfContained /
/// PublishSingleFile / IncludeNativeLibrariesForSelfExtract / PublishTrimmed；publish-app.ps1
/// 与 publish.sh 的 Worker 一条 dotnet publish 调用必须带齐三枚 /p: 标志和 --self-contained，
/// 且脚本默认 self-contained=true。
/// #12 收尾：脚本段补 --self-contained 断言（原先只查三枚 /p:，self-contained 主张无断言）；
/// 扫描面补 props / targets（原先只扫 src 的 csproj，Directory.Build.props 又犯老毛病也看不见）；
/// 工程文件判定升级为 XML 解析（子串匹配会被 XML 注释残留喂红、被 Condition=false 绕过）；
/// 补脚本默认值断言（默认被改掉时 publish-e2e 不显式传开关，产物静默变 framework-dependent）。
/// PublishReadyToRun 不在本检查四属性之列——#12 裁决其中心化在 Directory.Build.props，
/// 单一来源由 Net10CompatibilityGuardTests 锁定。
/// 旧 Ui 工程退役（#61）后 UI 侧 dotnet publish 消失：GUI 入口改为 cargo 构建的
/// photoprivacy-ui（Windows 走 windows-gnu target + windres，见 ui/crates/photoprivacy-ui/build.rs），
/// 复制为 PhotoPrivacy(.exe) 作为发布入口——本检查同时钉住这条接法不被回退。
/// </summary>
public sealed class PublishPropertiesCsprojGuardTests
{
    private static readonly string[] PublishProperties =
    [
        "SelfContained",
        "PublishSingleFile",
        "IncludeNativeLibrariesForSelfExtract",
        "PublishTrimmed",
    ];

    private static readonly string[] PublishFlagTokens =
    [
        "/p:PublishSingleFile=true",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:PublishTrimmed=false",
        // self-contained 经 --self-contained 开关传（值由脚本参数推导），不走 /p:，故 token 只锚开关名。
        "--self-contained",
    ];

    internal static List<string> ClassifyProjectFilePublishProperties(string content, string label)
    {
        // 要防的老毛病（#12）：脚本传入的四发布属性又写回生产工程文件——csproj 直接持有、
        // 根 props/targets 自动导入、src 子目录 props/targets 覆盖。此前为子串匹配：XML 注释里的
        // 示例残留会喂红，<Prop Condition="…"> 的条件定义会被子串绕过。
        // 处置：XML 解析取定义元素——注释节点不计；带 Condition 的定义从严报红
        // （MSBuild 条件不静态求值，宁可误红交人复核，不放走过任何持有形态）。
        // 关联 issue #12。
        XDocument doc;
        try
        {
            doc = XDocument.Parse(content);
        }
        catch (XmlException)
        {
            return [label + ": 非合法 XML（工程文件损坏，MSBuild 同样无法求值）"];
        }

        var violations = new List<string>();
        foreach (var prop in PublishProperties)
        {
            if (doc.Descendants().Any(e => e.Name.LocalName == prop))
            {
                violations.Add(label + ": 工程文件定义发布属性 <" + prop +
                               ">（发布属性只在发布脚本传递；PublishReadyToRun 例外中心化于 Directory.Build.props，#12）");
            }
        }

        return violations;
    }

    internal static List<string> ClassifyScriptSelfContainedDefault(string script, string label)
    {
        // 要防的老毛病（#12）：发布脚本 self-contained 默认值被改掉或摘除——publish-e2e
        // 与发版链都不显式传 --self-contained，走的就是这个默认；默认一变，产物静默变
        // framework-dependent，self-contained 主张失效。期望形态按脚本绑定（ps1 参数默认值 /
        // sh 的 SELF_CONTAINED 变量），不做跨形态 OR——任一脚本出现另一平台的字样不算绿。
        // 字面钉扎按 R2「保留」档做 characterization：这是变更探测器——默认声明等价重排
        // （空格/引号）会误红，测试失败时先按 R2 复核三档。关联 issue #12。
        var expectedDefault = label.Contains("publish.sh", StringComparison.Ordinal)
            ? "SELF_CONTAINED=\"true\""
            : "$SelfContained = \"true\"";
        var violations = new List<string>();
        if (!script.Contains(expectedDefault, StringComparison.Ordinal))
        {
            violations.Add(label + " 的 self-contained 默认值不是 true（脚本默认须 self-contained，#12）");
        }

        return violations;
    }

    internal static List<string> ClassifyPublishScript(string script, string label)
    {
        // Worker 的 dotnet publish 调用段必须带齐三枚 /p: 标志和 --self-contained；UI 侧走 cargo（见 ClassifyCargoUiEntry）。
        // 先剥脚本注释（PowerShell/shell 的 # 行注释）：注释里残留的 token 不得让检查骗绿（#61）。
        // 残余（#12 登记）：调用段是「本条 dotnet publish 起至下一条或脚本尾」，且
        // --self-contained 只锚开关名不校验取值绑定——调用行被摘而后续非注释行残留该串时
        // 会代绿。脚本尾部无该串的现实下不构成漏洞，等真犯了再处理。
        script = StripHashComments(script);
        var violations = new List<string>();
        var indices = new List<int>();
        var pos = 0;
        const string marker = "dotnet publish";
        while ((pos = script.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
        {
            // 只认行首调用（允许前导空白）——排除 throw "dotnet publish (worker) failed" 一类字符串。
            var lineStart = script.LastIndexOf('\n', pos) + 1;
            if (script[lineStart..pos].Trim().Length == 0)
            {
                indices.Add(pos);
            }

            pos += marker.Length;
        }

        for (var i = 0; i < indices.Count; i++)
        {
            var end = i + 1 < indices.Count ? indices[i + 1] : script.Length;
            var block = script[indices[i]..end];
            foreach (var token in PublishFlagTokens)
            {
                if (!block.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add(label + " 第 " + (i + 1) + " 条 dotnet publish 调用缺少 " + token);
                }
            }
        }

        if (indices.Count != 1)
        {
            violations.Add(label + " dotnet publish 调用数应为 1（Worker；GUI 入口走 cargo 构建的 photoprivacy-ui），实测 " + indices.Count);
        }

        violations.AddRange(ClassifyCargoUiEntry(script, label));
        violations.AddRange(ClassifyScriptSelfContainedDefault(script, label));
        return violations;
    }

    internal static List<string> ClassifyCargoUiEntry(string script, string label)
    {
        // 入口接法检查（#61）：两个发布脚本都必须以 cargo 构建 photoprivacy-ui 并复制为 PhotoPrivacy 入口。
        // 剥 # 行注释后再判：退出条件 = 构建行 + 复制引用各一处代码级出现；只出现在注释/文案里的
        // 单次引用不足以骗绿（code-review 判定性意见的处置）。
        script = StripHashComments(script);
        var violations = new List<string>();
        if (!script.Contains("cargo build --release --locked", StringComparison.Ordinal))
        {
            violations.Add(label + " 缺少 cargo build --release --locked（GUI 入口构建）");
        }
        var uiRefs = CountOccurrences(script, "photoprivacy-ui");
        if (uiRefs < 2)
        {
            violations.Add(label + " photoprivacy-ui 产物引用不足 2 处（构建行 + 复制引用），实测 " + uiRefs);
        }
        if (!script.Contains("PhotoPrivacy.exe", StringComparison.Ordinal) &&
            !script.Contains("APP_EXE=\"PhotoPrivacy\"", StringComparison.Ordinal))
        {
            violations.Add(label + " 缺少 PhotoPrivacy 入口命名（默认入口必须保持 PhotoPrivacy(.exe)）");
        }
        return violations;
    }

    /// <summary>
    /// 剥除 shell/PowerShell 的整行 # 注释（首个非空白字符为 #）。
    /// 只认整行注释：shell 的 <c>$#</c> 参数展开不是注释，按行内 # 截断会破坏脚本扫描面。
    /// </summary>
    internal static string StripHashComments(string script)
    {
        var lines = script.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Select(line =>
            line.TrimStart().StartsWith('#') ? string.Empty : line));
    }

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

    // 要防的老毛病（#12）：SelfContained / PublishSingleFile / IncludeNativeLibrariesForSelfExtract /
    // PublishTrimmed 又写回生产工程文件——csproj 直接持有、根 Directory.Build.props /
    // Directory.Build.targets 自动导入（全工程生效）、src 子目录 props / targets 覆盖；发布属性只在发布脚本传递。
    // PublishReadyToRun 例外：#12 裁决中心化于根 Directory.Build.props，不在本检查四属性之列
    // （单一来源由 Net10CompatibilityGuardTests 锁定），此处合法存在不得报红。
    // 读取走 SourceLint.Read 单一实现；XML 注释由解析器天然无视，不走 ReadStripped
    // （剥 // 会破坏 XML 内 URL 字面量）。关联 issue #12。
    [Fact]
    public void Production_Project_Files_Should_Not_Hold_Publish_Properties()
    {
        var violations = new List<string>();
        foreach (var file in SourceLint.EnumerateSourceFiles("src", "*.csproj")
                     .Concat(SourceLint.EnumerateSourceFiles("src", "*.props"))
                     .Concat(SourceLint.EnumerateSourceFiles("src", "*.targets")))
        {
            var relative = Path.GetRelativePath(SourceLint.RepoRoot, file);
            violations.AddRange(ClassifyProjectFilePublishProperties(
                SourceLint.Read(relative), relative));
        }

        foreach (var rootImport in new[] { "Directory.Build.props", "Directory.Build.targets" })
        {
            var path = Path.Combine(SourceLint.RepoRoot, rootImport);
            if (File.Exists(path))
            {
                violations.AddRange(ClassifyProjectFilePublishProperties(
                    SourceLint.Read(rootImport), rootImport));
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Publish_Scripts_Should_Pass_SingleFile_Flags_And_Wire_Cargo_Ui_Entry()
    {
        var violations = new List<string>();
        violations.AddRange(ClassifyPublishScript(
            SourceLint.Read("scripts", "publish-app.ps1"), "publish-app.ps1"));
        violations.AddRange(ClassifyPublishScript(
            SourceLint.Read("scripts", "publish.sh"), "publish.sh"));
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Publish_Properties_Guard_Negative_Self_Proof()
    {
        // 失效即红自我验证：工程文件老毛病再犯（csproj / props）/ 脚本掉标志 / 默认值漂移 / 入口接法回退各类篡改必须报红，基线必须绿。
        var failures = new List<string>();

        const string cleanCsproj = "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";
        if (ClassifyProjectFilePublishProperties(cleanCsproj, "x.csproj").Count != 0)
        {
            failures.Add("baseline csproj 误报");
        }

        var driftCsproj = cleanCsproj.Replace(
            "</PropertyGroup>", "<PublishSingleFile>true</PublishSingleFile></PropertyGroup>",
            StringComparison.Ordinal);
        if (ClassifyProjectFilePublishProperties(driftCsproj, "x.csproj").Count == 0)
        {
            failures.Add("MISSED: csproj 回潮 <PublishSingleFile> 未红");
        }

        // #12：props 扫描面的红/绿自我验证——基线 Directory.Build.props 只含中心化的
        // PublishReadyToRun，必须绿；脚本传入的四属性之一又写回 props 必须红。
        var greenProps = SourceLint.Read("Directory.Build.props");
        if (ClassifyProjectFilePublishProperties(greenProps, "Directory.Build.props").Count != 0)
        {
            failures.Add("baseline Directory.Build.props 误报（PublishReadyToRun 为 #12 裁决的中心化例外）");
        }

        var driftProps = greenProps.Replace(
            "</PropertyGroup>", "<PublishTrimmed>true</PublishTrimmed></PropertyGroup>",
            StringComparison.Ordinal);
        if (ClassifyProjectFilePublishProperties(driftProps, "Directory.Build.props").Count == 0)
        {
            failures.Add("MISSED: props 回潮 <PublishTrimmed> 未红");
        }

        // #12：XML 注释残留不得误红；带 Condition 的定义必须红（从严——条件不静态求值）。
        const string commentDrift = "<Project><PropertyGroup><OutputType>Exe</OutputType>" +
                                    "<!-- <PublishTrimmed>true</PublishTrimmed> --></PropertyGroup></Project>";
        if (ClassifyProjectFilePublishProperties(commentDrift, "x.csproj").Count != 0)
        {
            failures.Add("baseline csproj XML 注释残留误报");
        }

        const string conditionDrift = "<Project><PropertyGroup><OutputType>Exe</OutputType>" +
                                      "<PublishTrimmed Condition=\"'$(Never)' == 'true'\">true</PublishTrimmed></PropertyGroup></Project>";
        if (ClassifyProjectFilePublishProperties(conditionDrift, "x.csproj").Count == 0)
        {
            failures.Add("MISSED: 带条件的 <PublishTrimmed> 定义未红");
        }

        var greenScript = SourceLint.Read("scripts", "publish-app.ps1");
        if (ClassifyPublishScript(greenScript, "publish-app.ps1").Count != 0)
        {
            failures.Add("baseline publish-app.ps1 未绿");
        }

        var greenSh = SourceLint.Read("scripts", "publish.sh");
        if (ClassifyPublishScript(greenSh, "publish.sh").Count != 0)
        {
            failures.Add("baseline publish.sh 未绿");
        }

        var idx = greenScript.IndexOf("/p:PublishSingleFile=true", StringComparison.Ordinal);
        if (idx < 0)
        {
            failures.Add("MUTATION DID NOT APPLY: publish-app.ps1 无 /p:PublishSingleFile=true");
        }
        else
        {
            var tampered = greenScript[..idx] + greenScript[(idx + "/p:PublishSingleFile=true".Length)..];
            if (ClassifyPublishScript(tampered, "publish-app.ps1").Count == 0)
            {
                failures.Add("MISSED: Worker publish 调用掉标志未红");
            }
        }

        // #12：self-contained 断言的失效自我验证——两个脚本调用段掉 --self-contained、
        // 默认值改 false / 摘除，均必须红。调用段定位从 dotnet publish 之后起查，
        // 避开 publish.sh usage 文案里的 --self-contained。
        static void AssertMissingSelfContainedTurnsRed(string green, string label, List<string> failures)
        {
            var pubIdx = green.IndexOf("dotnet publish", StringComparison.Ordinal);
            var scIdx = pubIdx >= 0 ? green.IndexOf("--self-contained", pubIdx, StringComparison.Ordinal) : -1;
            if (scIdx < 0)
            {
                failures.Add("MUTATION DID NOT APPLY: " + label + " 调用段无 --self-contained");
                return;
            }

            var tampered = green[..scIdx] + green[(scIdx + "--self-contained".Length)..];
            if (ClassifyPublishScript(tampered, label).Count == 0)
            {
                failures.Add("MISSED: " + label + " Worker publish 调用掉 --self-contained 未红");
            }
        }

        AssertMissingSelfContainedTurnsRed(greenScript, "publish-app.ps1", failures);
        AssertMissingSelfContainedTurnsRed(greenSh, "publish.sh", failures);

        var defaultFlipped = greenScript.Replace(
            "$SelfContained = \"true\"", "$SelfContained = \"false\"", StringComparison.Ordinal);
        if (defaultFlipped == greenScript)
        {
            failures.Add("MUTATION DID NOT APPLY: publish-app.ps1 无 $SelfContained = \"true\"");
        }
        else if (ClassifyPublishScript(defaultFlipped, "publish-app.ps1").Count == 0)
        {
            failures.Add("MISSED: publish-app.ps1 self-contained 默认值改 false 未红");
        }

        var shDefaultRemoved = greenSh.Replace("SELF_CONTAINED=\"true\"", "", StringComparison.Ordinal);
        if (shDefaultRemoved == greenSh)
        {
            failures.Add("MUTATION DID NOT APPLY: publish.sh 无 SELF_CONTAINED=\"true\"");
        }
        else if (ClassifyPublishScript(shDefaultRemoved, "publish.sh").Count == 0)
        {
            failures.Add("MISSED: publish.sh self-contained 默认值摘除未红");
        }

        var entryDrift = greenScript.Replace("photoprivacy-ui", "old-ui", StringComparison.Ordinal);
        if (ClassifyCargoUiEntry(entryDrift, "publish-app.ps1").Count == 0)
        {
            failures.Add("MISSED: GUI 入口产物被改写未红");
        }

        // 注释骗绿自我验证：入口 token 只留在 # 注释里（代码行被删）时必须报红。
        var commentOnly = "# cargo build --release --locked -p photoprivacy-ui\n" +
                          "# cp ... photoprivacy-ui.exe ... PhotoPrivacy.exe\n";
        if (ClassifyCargoUiEntry(commentOnly, "comment-only").Count == 0)
        {
            failures.Add("MISSED: 仅注释中的入口 token 未被剥除");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
