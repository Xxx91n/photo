using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （#65）Design 决策标记形态守卫：#52 定的 <c>// Design(#&lt;issue&gt;): </c> 约定，
/// 它的可检索性与"标记紧贴受约束声明"这条形态纪律由本族断言兜住，不交给 reviewer 的记忆。
/// 只扫 src/，测试自身对标记字面量的引用不计入。
/// </summary>
public sealed class DesignMarkerSourceTests
{
    /// <summary>
    /// src/ 标记数下限。#52 首批为 5 处，其中 2 处（AppComposition.cs / Program.cs）住在旧 Ui 工程，
    /// 随 #61 切换日退役——下限相应调整为存活的 3 处；低于此数说明约定被整体回退。
    /// </summary>
    private const int MarkerFloor = 3;

    private static readonly Regex CanonicalMarker =
        new(@"^\s*//\s*Design\(#\d+\): \S", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>候选=「像是想写标记」的行，含 <c>Design (#65)</c> 这类多一个空格的走形；放宽检测才能让形态断言拒掉它。</summary>
    private static readonly Regex MarkerCandidate =
        new(@"Design\s*\(#", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private sealed record Marker(string File, int Line, string Text, string? NextLine);

    private static List<Marker> ScanMarkers()
    {
        var found = new List<Marker>();
        foreach (var file in SourceLint.SrcCsFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!MarkerCandidate.IsMatch(lines[i]))
                {
                    continue;
                }

                found.Add(new Marker(
                    Path.GetRelativePath(SourceLint.RepoRoot, file).Replace('\\', '/'),
                    i + 1,
                    lines[i].Trim(),
                    i + 1 < lines.Length ? lines[i + 1].Trim() : null));
            }
        }

        return found;
    }

    private static string Describe(Marker marker) => $"{marker.File}:{marker.Line}  {marker.Text}";

    [Fact]
    public void Design_Marker_Should_Use_The_Canonical_Form()
    {
        // 防的具体回潮形态：标记写成 `// Design(#49) 换栈主缝`（漏掉 `: `）、`// Design(#<issue>): `（占位符没替换真票号）、
        // 或改用 `/* Design(#49) */` 块注释 —— 三种都能被 grep `Design(#` 命中，却把"按票号解析决策索引"的口径敲碎。
        // 关联 issue #65。
        var offenders = ScanMarkers().Where(m => !CanonicalMarker.IsMatch(m.Text)).ToList();
        Assert.True(offenders.Count == 0,
            $"Design 标记必须是 `// Design(#<digits>): <正文>` 形态：\n{string.Join("\n", offenders.Select(Describe))}");
    }

    [Fact]
    public void Design_Marker_Should_Sit_Immediately_Above_The_Governed_Declaration()
    {
        // 防的具体回潮形态：#52 首批落地时把标记插在 AppComposition 与 NamedPipeIpcTransport.ServerOptions 的 /// 文档块之上
        // （PR #64 评审点名才修）—— grep 命中的标记指向一段文档注释而非受约束声明，"决策贴着代码"退化成"决策贴着注释"；
        // 空行隔开、下方压一行 /* 块注释 */ 同罪。关联 issue #65。
        var offenders = ScanMarkers()
            .Where(m => CanonicalMarker.IsMatch(m.Text))
            .Where(m => string.IsNullOrEmpty(m.NextLine) ||
                        m.NextLine.StartsWith("//", StringComparison.Ordinal) ||
                        m.NextLine.StartsWith("/*", StringComparison.Ordinal))
            .ToList();
        Assert.True(offenders.Count == 0,
            $"Design 标记的紧邻下一行必须是受约束声明（不得是空行、// 或 /// 注释行、/* 块注释）：\n{string.Join("\n", offenders.Select(Describe))}");
    }

    [Fact]
    public void Design_Markers_Should_Not_Vanish()
    {
        // 防的具体回潮形态：整批标记被手工删光或被格式化/重排脚本吃掉，GLOSSARY 词条还写着约定生效、守卫却一条都不红。
        // 关联 issue #65。
        var count = ScanMarkers().Count(m => CanonicalMarker.IsMatch(m.Text));
        Assert.True(count >= MarkerFloor,
            $"src/ 里的 Design 标记不得少于 {MarkerFloor} 处（#52 首批 5 处中 2 处随旧 Ui 工程退役，#61；当前 {count} 处）");
    }
}
