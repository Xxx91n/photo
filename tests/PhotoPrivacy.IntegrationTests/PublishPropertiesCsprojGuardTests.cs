namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 票 06（correctness-round 顺带发现收口 / ADR 0035、ADR 0012 修订口径）：
/// 发布属性单一来源守卫——生产 csproj 不得直接持有 SelfContained / PublishSingleFile /
/// IncludeNativeLibrariesForSelfExtract / PublishTrimmed；publish-app.ps1 与 publish.sh 的
/// UI 与 Worker 两条 dotnet publish 调用都必须带齐三枚 /p: 标志（UI 侧原由 csproj 直接持有，
/// 本票上提至脚本后，ADR 0035「csproj 不持有发布属性」口径在 UI 侧亦成立）。
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
                               ">（ADR 0035：发布属性只在发布脚本 /p: 传递）");
            }
        }

        return violations;
    }

    internal static List<string> ClassifyPublishScript(string script, string label)
    {
        // 每条 dotnet publish 调用段都必须带齐三枚 /p: 标志（UI 与 Worker 两条调用全覆盖）。
        var violations = new List<string>();
        var indices = new List<int>();
        var pos = 0;
        const string marker = "dotnet publish";
        while ((pos = script.IndexOf(marker, pos, StringComparison.Ordinal)) >= 0)
        {
            // 只认行首调用（允许前导空白）——排除 throw "dotnet publish (ui) failed" 一类字符串。
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

        if (indices.Count != 2)
        {
            violations.Add(label + " dotnet publish 调用数应为 2（UI + Worker），实测 " + indices.Count);
        }

        return violations;
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
    public void Publish_Scripts_Should_Pass_SingleFile_Flags_For_Both_Projects()
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
        // 失效即红自证：csproj 回潮 / 脚本掉标志两类篡改必须报红，基线必须绿。
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
                failures.Add("MISSED: UI publish 调用掉标志未红");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
