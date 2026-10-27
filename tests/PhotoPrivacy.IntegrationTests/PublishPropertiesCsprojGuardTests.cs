namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （correctness-round 顺带发现收口；#61 切换日对账）：
/// 发布属性单一来源守卫——生产 csproj 不得直接持有 SelfContained / PublishSingleFile /
/// IncludeNativeLibrariesForSelfExtract / PublishTrimmed；publish-app.ps1 与 publish.sh 的
/// Worker 一条 dotnet publish 调用必须带齐三枚 /p: 标志。
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
    ];

    internal static List<string> ClassifyCsprojPublishProperties(string csprojContent, string label)
    {
        var violations = new List<string>();
        foreach (var prop in PublishProperties)
        {
            if (csprojContent.Contains("<" + prop + ">", StringComparison.Ordinal))
            {
                violations.Add(label + ": csproj 直接持有发布属性 <" + prop +
                               ">（发布属性只在发布脚本 /p: 传递）");
            }
        }

        return violations;
    }

    internal static List<string> ClassifyPublishScript(string script, string label)
    {
        // Worker 的 dotnet publish 调用段必须带齐三枚 /p: 标志；UI 侧走 cargo（见 ClassifyCargoUiEntry）。
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
        // 入口接法守卫：两个发布脚本都必须以 cargo 构建 photoprivacy-ui 并复制为 PhotoPrivacy 入口。
        // 出现次数 ≥2（cargo -p 构建行 + 产物复制引用各自独立一处）：只出现在注释/文案里的单次
        // 引用不足以骗绿（code-review 判定性意见的处置）。
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
        // 失效即红自证：csproj 回潮 / 脚本掉标志 / 入口接法回退三类篡改必须报红，基线必须绿。
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

        var entryDrift = greenScript.Replace("photoprivacy-ui", "old-ui", StringComparison.Ordinal);
        if (ClassifyCargoUiEntry(entryDrift, "publish-app.ps1").Count == 0)
        {
            failures.Add("MISSED: GUI 入口产物被改写未红");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
