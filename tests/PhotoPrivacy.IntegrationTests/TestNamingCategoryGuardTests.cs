using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// source-lint 守卫（#14 测试命名/分类统一治理）：钉住 tests/ 的测试类命名与 Category trait 口径。
/// 口径正文见 tests/TEST-CONVENTIONS.md §「命名与分类」；trait 取值域与目录对齐契约的事实源是
/// .github/workflows/ci.yml / release.yml 的 --filter（Category!=Smoke&Category!=ExifTool）。
/// 嵌套/文件级 fake、stub、fixture 等辅助类不受命名规则约束（xUnit 只要求测试类本身 public）。
/// </summary>
public sealed class TestNamingCategoryGuardTests
{
    /// <summary>public 测试类声明（xUnit 可发现形态）；internal 辅助类不匹配。partial 一并纳入（CodeRabbit #233 审查补漏）。</summary>
    private static readonly Regex PublicClassPattern =
        new(@"public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(\w+)",
            RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>
    /// 测试特性声明：覆盖无参/带参（[Fact(Skip=…)]）、命名空间限定（[Xunit.Fact]）与
    /// Attribute 后缀（[FactAttribute]）等 C# 合法写法（CodeRabbit #233 两轮审查补漏）。
    /// 仍不覆盖 global:: 全限定等更冷僻形态——仓内从未出现，回潮即撞守卫注释可见。
    /// 分片拼接防本文件自匹配。
    /// </summary>
    private static readonly Regex TestAttributePattern =
        new("\\[" + @"\s*(?:Xunit\.)?(?:Fact|Theory)(?:Attribute)?\b",
            RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>Category trait 声明。分片拼接是刻意的：本文件源码不得出现连续的 [Trait( 字面量，否则守卫自己踩线。</summary>
    private static readonly Regex CategoryTraitPattern =
        new("\\[" + "Trait" + @"\(\s*""Category""\s*,\s*""([^""]*)""\s*\)",
            RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    // 同上：口径字面量不在本文件连续出现——目录对齐检查复用 CategoryTraitPattern 的抓取结果。

    [Fact]
    public void TestFixture_Class_Names_Should_End_With_Tests()
    {
        // 防的具体回潮形态：新增测试类名不按 *Tests 收尾（存量先例——单数的 RealExifToolProbeTest 混进
        // 全仓 *Tests 家族），dotnet test 的 FullyQualifiedName 口径与 CI 过滤器、审查引用对不上，排查成本翻倍。
        // 关联 issue #14。
        var violations = new List<string>();
        foreach (var (relPath, content) in EnumerateTestSources())
        {
            if (!TestAttributePattern.IsMatch(content))
            {
                continue;
            }

            foreach (Match m in PublicClassPattern.Matches(content))
            {
                var name = m.Groups[1].Value;
                if (name.EndsWith("Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add($"{relPath}: public 测试类 {name} 未以 Tests 结尾");
            }
        }

        Assert.True(violations.Count == 0,
            "tests/ 下含 [Fact]/[Theory] 的文件，其 public 测试类声明必须以 Tests 结尾（辅助类请用 internal）：\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void Category_Trait_Values_Should_Stay_Canonical()
    {
        // 防的具体回潮形态：有人自造第四个 Category 值（如 Unit/Integration/Guard），而 ci.yml 与 release.yml
        // 的过滤器只认 Category!=Smoke&Category!=ExifTool——新值静默落进默认 job 或被显式过滤漏掉，分类契约失效。
        // 关联 issue #14。
        var violations = new List<string>();
        foreach (var (relPath, content) in EnumerateTestSources())
        {
            foreach (Match m in CategoryTraitPattern.Matches(content))
            {
                var category = m.Groups[1].Value;
                if (category is not ("Smoke" or "ExifTool"))
                {
                    violations.Add($"{relPath}: Category 取值 \"{category}\" 不在口径 {{Smoke, ExifTool}} 内");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Category trait 只允许 Smoke / ExifTool 两个值（缺省 trait = 默认 CI job 跑的常规测试）：\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void Smoke_And_ExifTool_Directories_Should_Match_Category_Trait()
    {
        // 防的具体回潮形态：目录与 trait 各自漂移——ExifTool 探针混进 Smoke\ 目录（存量先例
        // RealExifToolProbeTest，Category=ExifTool 却住 Smoke\，按目录找用例找不到）；或 source-lint 守卫
        // （默认 job 形态）误放 Smoke\ 目录却不带 trait（存量先例 ReleaseReadinessFullCaptureGuardTests）。
        // 双向对齐：目录 ⇒ 必带对应 trait；trait ⇒ 必住在对应目录。关联 issue #14。
        var violations = new List<string>();
        foreach (var (relPath, content) in EnumerateTestSources())
        {
            if (!relPath.Contains("PhotoPrivacy.IntegrationTests", StringComparison.Ordinal))
            {
                continue;
            }

            var inSmokeDir = relPath.Contains($"{Path.DirectorySeparatorChar}Smoke{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            var inExifToolDir = relPath.Contains($"{Path.DirectorySeparatorChar}ExifTool{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            var categories = CategoryTraitPattern.Matches(content)
                .Select(m => m.Groups[1].Value)
                .ToList();

            if (inSmokeDir && !categories.Contains("Smoke"))
            {
                violations.Add($"{relPath}: 住 Smoke\\ 目录但未带 Category=Smoke trait");
            }

            if (inExifToolDir && !categories.Contains("ExifTool"))
            {
                violations.Add($"{relPath}: 住 ExifTool\\ 目录但未带 Category=ExifTool trait");
            }

            foreach (var category in categories)
            {
                var dirMismatch = category switch
                {
                    "Smoke" => !inSmokeDir,
                    "ExifTool" => !inExifToolDir,
                    _ => false,
                };
                if (dirMismatch)
                {
                    violations.Add($"{relPath}: Category={category} 的文件必须住在对应 {category}\\ 目录下");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "IntegrationTests 内 Smoke\\/ExifTool\\ 目录与 Category trait 必须双向对齐：\n"
            + string.Join("\n", violations));
    }

    /// <summary>枚举 tests/ 全部 .cs（排除 bin/obj/target），返回（仓库相对路径, 剥注释后全文）。</summary>
    private static List<(string RelPath, string Content)> EnumerateTestSources()
    {
        var sources = new List<(string, string)>();
        foreach (var file in SourceLint.EnumerateSourceFiles("tests", "*.cs"))
        {
            var relPath = Path.GetRelativePath(SourceLint.RepoRoot, file);
            sources.Add((relPath, SourceLint.ReadStripped(relPath)));
        }

        return sources;
    }
}
