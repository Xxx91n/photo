namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （correctness-round 顺带发现收口；#61 切换日对账）：
/// 发布属性单一来源守卫——生产工程文件（csproj 与 props）不得直接持有 SelfContained /
/// PublishSingleFile / IncludeNativeLibrariesForSelfExtract / PublishTrimmed；publish-app.ps1
/// 与 publish.sh 的 Worker 一条 dotnet publish 调用必须带齐三枚 /p: 标志和 --self-contained。
/// #12 收口：脚本段补 --self-contained 断言（原先只查三枚 /p:，self-contained 主张无断言），
/// 扫描面补 props 文件（原先只扫 src 的 csproj，Directory.Build.props 回潮看不见）；
/// PublishReadyToRun 不在本守卫四个属性之列——#12 裁决其中心化在 Directory.Build.props，
/// 单一来源由 Net10CompatibilityGuardTests 锁定。
/// 旧 Ui 工程退役（#61）后 UI 侧 dotnet publish 消失：GUI 入口改为 cargo 构建的
/// photoprivacy-ui（Windows 走 windows-gnu target + windres，见 ui/crates/photoprivacy-ui/build.rs），
/// 复制为 PhotoPrivacy(.exe) 作为发布入口——本守卫同时钉住这条接法不被回退。
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

    internal static List<string> ClassifyCsprojPublishProperties(string csprojContent, string label)
    {
        var violations = new List<string>();
        foreach (var prop in PublishProperties)
        {
            if (csprojContent.Contains("<" + prop + ">", StringComparison.Ordinal))
            {
                violations.Add(label + ": 工程文件直接持有发布属性 <" + prop +
                               ">（发布属性只在发布脚本传递；PublishReadyToRun 例外中心化于 Directory.Build.props，#12）");
            }
        }

        return violations;
    }

    internal static List<string> ClassifyPublishScript(string script, string label)
    {
        // Worker 的 dotnet publish 调用段必须带齐三枚 /p: 标志和 --self-contained；UI 侧走 cargo（见 ClassifyCargoUiEntry）。
        // 先剥脚本注释（PowerShell/shell 的 # 行注释）：注释里残留的 token 不得让守卫骗绿（#61）。
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
        return violations;
    }

    internal static List<string> ClassifyCargoUiEntry(string script, string label)
    {
        // 入口接法守卫（#61）：两个发布脚本都必须以 cargo 构建 photoprivacy-ui 并复制为 PhotoPrivacy 入口。
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

    [Fact]
    public void Production_Csproj_Should_Not_Hold_Publish_Properties()
    {
        var srcDir = Path.Combine(SourceLint.RepoRoot, "src");
        var violations = new List<string>();
        foreach (var file in Directory.GetFiles(srcDir, "*.csproj", SearchOption.AllDirectories))
        {
            violations.AddRange(ClassifyCsprojPublishProperties(
                File.ReadAllText(file), file[(srcDir.Length + 1)..]));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Production_Props_Files_Should_Not_Hold_Script_Passed_Publish_Properties()
    {
        // #12 守卫缺口收口：四个脚本传入的发布属性若回潮进根 Directory.Build.props 或 src 的
        // props 文件，csproj 扫描面看不见（Directory.Build.props 全工程导入，回潮面更大）。
        // PublishReadyToRun 例外：#12 裁决中心化于此，不在本守卫四属性之列（由
        // Net10CompatibilityGuardTests 锁单一来源），此处合法存在不得报红。
        var repoRoot = SourceLint.RepoRoot;
        var violations = new List<string>();
        var propsFiles = Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.props", SearchOption.AllDirectories)
            .Append(Path.Combine(repoRoot, "Directory.Build.props"));
        foreach (var file in propsFiles)
        {
            violations.AddRange(ClassifyCsprojPublishProperties(
                File.ReadAllText(file), file[(repoRoot.Length + 1)..]));
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
        // 失效即红自证：工程文件回潮（csproj / props）/ 脚本掉标志 / 入口接法回退各类篡改必须报红，基线必须绿。
        var failures = new List<string>();

        const string cleanCsproj = "<Project><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";
        if (ClassifyCsprojPublishProperties(cleanCsproj, "x.csproj").Count != 0)
        {
            failures.Add("baseline csproj 误报");
        }

        var driftCsproj = cleanCsproj.Replace(
            "</PropertyGroup>", "<PublishSingleFile>true</PublishSingleFile></PropertyGroup>",
            StringComparison.Ordinal);
        if (ClassifyCsprojPublishProperties(driftCsproj, "x.csproj").Count == 0)
        {
            failures.Add("MISSED: csproj 回潮 <PublishSingleFile> 未红");
        }

        // #12：props 扫描面的红/绿自证——基线 Directory.Build.props 只含中心化的
        // PublishReadyToRun，必须绿；回潮进脚本传入四属性之一必须红。
        var greenProps = SourceLint.Read("Directory.Build.props");
        if (ClassifyCsprojPublishProperties(greenProps, "Directory.Build.props").Count != 0)
        {
            failures.Add("baseline Directory.Build.props 误报（PublishReadyToRun 为 #12 裁决的中心化例外）");
        }

        var driftProps = greenProps.Replace(
            "</PropertyGroup>", "<PublishTrimmed>true</PublishTrimmed></PropertyGroup>",
            StringComparison.Ordinal);
        if (ClassifyCsprojPublishProperties(driftProps, "Directory.Build.props").Count == 0)
        {
            failures.Add("MISSED: props 回潮 <PublishTrimmed> 未红");
        }

        var greenScript = SourceLint.Read("scripts", "publish-app.ps1");
        if (ClassifyPublishScript(greenScript, "publish-app.ps1").Count != 0)
        {
            failures.Add("baseline publish-app.ps1 未绿");
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

        // #12：self-contained 断言的失效自证——调用段掉 --self-contained 必须红。
        var scIdx = greenScript.IndexOf("--self-contained", StringComparison.Ordinal);
        if (scIdx < 0)
        {
            failures.Add("MUTATION DID NOT APPLY: publish-app.ps1 无 --self-contained");
        }
        else
        {
            var scTampered = greenScript[..scIdx] + greenScript[(scIdx + "--self-contained".Length)..];
            if (ClassifyPublishScript(scTampered, "publish-app.ps1").Count == 0)
            {
                failures.Add("MISSED: Worker publish 调用掉 --self-contained 未红");
            }
        }

        var entryDrift = greenScript.Replace("photoprivacy-ui", "old-ui", StringComparison.Ordinal);
        if (ClassifyCargoUiEntry(entryDrift, "publish-app.ps1").Count == 0)
        {
            failures.Add("MISSED: GUI 入口产物被改写未红");
        }

        // 注释骗绿自证：入口 token 只留在 # 注释里（代码行被删）时必须报红。
        var commentOnly = "# cargo build --release --locked -p photoprivacy-ui\n" +
                          "# cp ... photoprivacy-ui.exe ... PhotoPrivacy.exe\n";
        if (ClassifyCargoUiEntry(commentOnly, "comment-only").Count == 0)
        {
            failures.Add("MISSED: 仅注释中的入口 token 未被剥除");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
