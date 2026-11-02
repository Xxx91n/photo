using System.Text;
using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// （#63）不变量编号引用对账守卫：src/ 与 tests/ 的 .cs、ui/ 的 .rs/.toml 里出现的
/// 「不变量N」编号引用，必须在 GLOSSARY.md「不变量①–⑤（运行不变量表）」词条的表体里
/// 有定义行。
///
/// 防的故障模式即本票处理的存量形态：fa360d6 拆治理层后，五不变量表体（D 系决策账本
/// D-002，从未入仓）散佚，源码 11 处编号引用悬空——「不变量④」读不出指什么。表体已随
/// 本票重建进 GLOSSARY；本守卫防同一故障模式回潮：新增编号引用（含未来的 ⑥⑦…）
/// 而表体不收录时随 CI 硬失败。
///
/// 识别口径（变更探测器，非契约）：引用行 = 含「不变量」字样且带圆圈数字的行，行上全部
/// 圆圈数字按引用计——覆盖「不变量①④」连写与「不变量①fail-closed ④驻守可观测」同句间隔
/// 两种存量形态；纯枚举用圆圈数字的行（如 WorkerIpcServerHostedService 的 ①② 行为清单）
/// 不含「不变量」字样，不进候选；同句混排枚举与引用会过计，撞红按 TEST-CONVENTIONS R2 处置。
/// 定义口径：GLOSSARY 表体行 = `- **不变量N（名称）**：` 列表行（词条导语行无列表前缀，不匹配）。
/// ③ 在现行代码零引用点属预期（考古恢复，见词条正文），故对账为单向包含：引用集 ⊆ 定义集。
/// 读原始行不剥注释（TEST-CONVENTIONS §3 的必要偏离）：引用本体住在 /// 与 // 里，
/// 剥除即扫描恒空（主 Fact 的 NotEmpty 会红，偏离可见、自保护）。
/// </summary>
public sealed class InvariantReferenceReconciliationTests
{
    /// <summary>圆圈数字 ①–⑳（U+2460–U+2473）：引用与定义共用的编号字符域，未来 ⑥⑦… 同域可判。</summary>
    private static readonly Regex CircledNumber = new(@"[①-⑳]", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    /// <summary>GLOSSARY 表体定义行：`- **不变量N（…）**：` 列表行。</summary>
    private static readonly Regex GlossaryDefinitionLine =
        new(@"^\s*-\s*\*\*不变量([①-⑳])", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private const string SelfFileName = "InvariantReferenceReconciliationTests.cs";

    private sealed record Reference(string File, int Line, string Text, IReadOnlyList<char> Numbers);

    /// <summary>
    /// 从单行文本提取被引用的不变量编号：行含「不变量」字样才把行上圆圈数字计为引用，
    /// 否则视为普通枚举编号（ExifToolBridge 的「否则①…②…」这类行不计）。
    /// </summary>
    private static HashSet<char> ExtractInvariantNumbers(string line)
    {
        if (!line.Contains("不变量", StringComparison.Ordinal))
        {
            return [];
        }

        return [.. CircledNumber.Matches(line).Select(m => m.Value[0])];
    }

    private static HashSet<char> ParseGlossaryDefinitions()
    {
        var defined = new HashSet<char>();
        var glossaryPath = Path.Combine(SourceLint.RepoRoot, "GLOSSARY.md");
        Assert.True(File.Exists(glossaryPath), "repo 根缺 GLOSSARY.md——不变量表体的存续出处没了（#63）");

        foreach (var line in File.ReadAllLines(glossaryPath, Encoding.UTF8))
        {
            var match = GlossaryDefinitionLine.Match(line);
            if (match.Success)
            {
                defined.Add(match.Groups[1].Value[0]);
            }
        }

        return defined;
    }

    private static List<Reference> ScanReferences()
    {
        // 根存在性断言（防半盲）：ui/ 或 tests/ 根漂移时枚举静默变空，主 Fact 的 NotEmpty 只防
        // 全盲不防半盲；src/ 根由 SrcCsFiles 自带的存在性断言兜住。关联 issue #63。
        Assert.True(Directory.Exists(Path.Combine(SourceLint.RepoRoot, "tests")), "tests/ root not found（#63）");
        Assert.True(Directory.Exists(Path.Combine(SourceLint.RepoRoot, "ui")), "ui/ root not found（#63）");

        var found = new List<Reference>();
        foreach (var file in ScannedFiles())
        {
            var lines = File.ReadAllLines(file, Encoding.UTF8);
            for (var i = 0; i < lines.Length; i++)
            {
                var numbers = ExtractInvariantNumbers(lines[i]);
                if (numbers.Count > 0)
                {
                    found.Add(new Reference(
                        Path.GetRelativePath(SourceLint.RepoRoot, file).Replace('\\', '/'),
                        i + 1,
                        lines[i].Trim(),
                        [.. numbers]));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// 扫描面 = src/ 与 tests/ 的手写 .cs + ui/ 的 .rs/.toml（枚举与构建产物排除统一走
    /// <see cref="SourceLint"/>，不在本守卫里另起一套）；本守卫文件自身排除——失效即红自证的
    /// 样本字面量含「不变量N」，会计数成假引用。
    /// </summary>
    private static IEnumerable<string> ScannedFiles()
    {
        foreach (var file in SourceLint.SrcCsFiles()
                     .Concat(SourceLint.EnumerateSourceFiles("tests", "*.cs"))
                     .Concat(SourceLint.EnumerateSourceFiles("ui", "*.rs"))
                     .Concat(SourceLint.EnumerateSourceFiles("ui", "*.toml")))
        {
            if (!string.Equals(Path.GetFileName(file), SelfFileName, StringComparison.Ordinal))
            {
                yield return file;
            }
        }
    }

    private static string Describe(Reference reference) =>
        $"{reference.File}:{reference.Line}  {reference.Text}";

    [Fact]
    public void Invariant_Number_References_Must_Be_Defined_In_The_Glossary_Table()
    {
        // 防的具体回潮形态：新增「不变量N」编号引用（含未来的 ⑥⑦…）而 GLOSSARY 表体未收录——
        // fa360d6 拆治理层后表体散佚、存量引用悬空，正是 #63 处理的故障模式；表体重建后本断言防
        // 悬空引用再次出现。空扫描同样判红：RepoRoot/目录布局漂移会让扫描静默变空（守卫失明）。
        // 关联 issue #63。
        var references = ScanReferences();
        Assert.NotEmpty(references);

        var defined = ParseGlossaryDefinitions();
        var offenders = references
            .Where(r => r.Numbers.Any(n => !defined.Contains(n)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "以下「不变量N」引用的编号未收录在 GLOSSARY「不变量①–⑤」表体里（悬空引用；"
            + "补定义行 `- **不变量N（名称）**：…` 或改用表内既有编号）：\n"
            + string.Join("\n", offenders.Select(Describe)));
    }

    [Fact]
    public void Glossary_Invariant_Table_Should_Define_The_Five_Known_Invariants()
    {
        // 防的具体回潮形态：表体定义行被整行删掉。①②④⑤ 有代码引用兜底（缺定义会让对账 Fact 红），
        // ③ 在现行代码零引用——删掉它没有任何引用侧信号，属恒绿盲区；表体自称 D-002 五不变量的
        // 对账重建，缺行即失真。关联 issue #63。
        var defined = ParseGlossaryDefinitions();
        foreach (var expected in "①②③④⑤")
        {
            Assert.True(defined.Contains(expected),
                $"GLOSSARY「不变量①–⑤」表体缺「{expected}」定义行（格式：`- **不变量N（名称）**：…`，#63）");
        }
    }

    [Fact]
    public void Reference_Extraction_Negative_Self_Proof()
    {
        // 失效即红自证：提取器必须认得两种存量引用形态（「不变量①④」连写、「不变量①… ④…」
        // 同句间隔），且放过不含「不变量」字样的纯枚举行与无编号的普通「不变量」措辞
        // （负样本均取自仓内真实行）——防「提取器恒空导致对账恒绿」的恒绿事故。关联 issue #63。
        var run = ExtractInvariantNumbers("（不变量①④）：排除清单的可见性告警");
        Assert.True(run.SetEquals("①④".ToCharArray()));

        var spaced = ExtractInvariantNumbers("（不变量①fail-closed ④驻守可观测）：挂死链行为测试");
        Assert.True(spaced.SetEquals("①④".ToCharArray()));

        Assert.Empty(ExtractInvariantNumbers("① 「accept 循环无 sync-over-async、重启重载不阻塞后续 accept」（WorkerIpcServerLoopTests）；"));
        Assert.Empty(ExtractInvariantNumbers("② 「ReloadConfig 真 await 而不是 sync-over-async」（WorkerIpcRequestHandlerTests）。"));
        Assert.Empty(ExtractInvariantNumbers("安全不变量：RAW 永不 -all=（MakerNotes 删后毁渲染），全擦意图展开为全部安全组。"));
    }
}
