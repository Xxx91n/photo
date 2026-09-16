using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.Configuration;

/// <summary>
/// 票 01（A-009 / 不变量⑤宣称-实现一致）：格式覆盖面对账断言——随票入 CI 硬失败。
///
/// 背景：收敛前 allowed_extensions 有 109 条，而 ExtensionFamilyMap 只有 35 条
/// （有效交集 34，差集 75 条无擦除策略）；README 则宣称“109 种扩展名”
/// 甚至“覆盖 ExifTool 98.2% 可写格式”。本守卫把宣称变成可机器验证的断言：
/// 1) allowed_extensions ⊆ ExtensionFamilyMap 键集，且集合差 = 0（宣称的东西必须有擦除策略）；
/// 2) ExtensionFamilyMap ⊆ allowed_extensions（映射面无死条目，jhc 类不可达条目不得回潮）；
/// 3) allowed_extensions 无重复条目；
/// 4) config.sample.json 与 AppConfig.Default 的 allowed_extensions 完全一致；
/// 5) README 的“NN 种扩展名”数字与实际条目数一致（数字由测试产出，不靠人手维护）。
/// </summary>
public sealed class FormatCoverageReconciliationTests
{
    private static string RepoRoot => FindRepoRoot();

    private static string Normalize(string extension)
        => extension.Trim().TrimStart('.').ToLowerInvariant();

    private static string[] DefaultAllowed
        => AppConfig.Default.Rules.AllowedExtensions.Select(Normalize).ToArray();

    private static HashSet<string> Mapped
        => WipeStrategyResolver.ExtensionFamilyMap.Keys.Select(Normalize).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Allowed_Extensions_Should_Be_Subset_Of_Wipe_Family_Map_With_Zero_Difference()
    {
        var claimedButUnmapped = DefaultAllowed
            .ToHashSet(StringComparer.Ordinal)
            .Except(Mapped)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            claimedButUnmapped.Length == 0,
            "allowed_extensions 里存在没有擦除策略映射的条目（宣称支持但实现不了）：" + string.Join(", ", claimedButUnmapped));
    }

    [Fact]
    public void Wipe_Family_Map_Should_Be_Subset_Of_Allowed_Extensions()
    {
        var mappedButNotAllowed = Mapped
            .Except(DefaultAllowed.ToHashSet(StringComparer.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            mappedButNotAllowed.Length == 0,
            "映射面存在不可达死条目（永远进不了 allowed_extensions）：" + string.Join(", ", mappedButNotAllowed));
    }

    [Fact]
    public void Default_Allowed_Extensions_Should_Not_Contain_Duplicates()
    {
        var duplicates = DefaultAllowed
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        Assert.True(duplicates.Length == 0, "allowed_extensions 含重复条目：" + string.Join(", ", duplicates));
    }

    [Fact]
    public void Sample_Config_Should_Match_Default_Allowed_Extensions()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "config", "config.sample.json")));

        var sampleAllowed = doc.RootElement
            .GetProperty("rules")
            .GetProperty("allowed_extensions")
            .EnumerateArray()
            .Select(x => Normalize(x.GetString() ?? string.Empty))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var defaultAllowed = DefaultAllowed.OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(defaultAllowed, sampleAllowed);
    }

    [Fact]
    public void Readme_Claimed_Extension_Count_Should_Match_Implementation()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        var match = Regex.Match(readme, @"\*\*(\d+)\s*种扩展名\*\*");

        Assert.True(match.Success, "README 缺少“**NN 种扩展名**”宣称行——宣称与实现对账失败");

        var claimed = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var actual = DefaultAllowed.Distinct(StringComparer.Ordinal).Count();

        Assert.Equal(actual, claimed);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PhotoPrivacy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new InvalidOperationException("PhotoPrivacy.sln not found upward from " + AppContext.BaseDirectory);
    }
}
