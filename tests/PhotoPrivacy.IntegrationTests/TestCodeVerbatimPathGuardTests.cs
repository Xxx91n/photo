using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 票 02 source-lint 守卫（承接票 01 首脑复核登记项）：测试代码禁止 drive-relative 形态的
/// verbatim 路径字面量 —— 形态为 at-verbatim 前缀 + 盘符 + 冒号 + 非分隔符字符。
///
/// 背景：票 01 提交把 RuleEngineTests 里的绝对路径字面量吞成了该形态（盘符后的分隔符丢失），
/// 而断言语义不依赖该路径（Decide 按扩展名判定），缺陷因此静默存活到首脑复核才被发现。
/// 该形态在任何平台都不是「绝对路径」的意图写法，出现即意味着反斜杠被吞或路径写错；
/// 本守卫把它变成 CI 红灯。整改方式：写绝对路径用完整形态（盘符 + 分隔符 + 路径），
/// 写相对路径就不要带盘符。
/// </summary>
public sealed class TestCodeVerbatimPathGuardTests
{
    // at-verbatim 前缀 + 盘符 + 冒号，且冒号后紧跟非分隔符字符 = drive-relative verbatim 字面量
    // （Windows 语义：相对「盘符当前目录」，不是绝对路径）
    private static readonly Regex DriveRelativeVerbatimLiteral = new(
        "@\"[A-Za-z]:(?![\\\\/])", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    [Fact]
    public void Test_Sources_Should_Not_Contain_Drive_Relative_Verbatim_Path_Literals()
    {
        var repoRoot = SourceLint.RepoRoot;
        var testsRoot = Path.Combine(repoRoot, "tests");
        Assert.True(Directory.Exists(testsRoot), $"tests root not found: {testsRoot}");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (DriveRelativeVerbatimLiteral.IsMatch(SourceLint.StripLineComment(lines[i])))
                {
                    violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "测试代码禁止 drive-relative verbatim 路径字面量（盘符后分隔符丢失的典型形态）：\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void Guard_Should_Detect_Drive_Relative_Literal_Shape()
    {
        // 失效即红预演（正向）：命中形态必须被抓到，否则守卫是空转。
        var hit = new string(['@', '"', 'D', ':', 'h', 'o', 't', '"']);
        Assert.Matches(DriveRelativeVerbatimLiteral, hit);
    }

    [Fact]
    public void Guard_Should_Accept_Absolute_And_Relative_Literal_Shapes()
    {
        // 失效即红预演（反向）：合法绝对路径形态与不带盘符的相对路径形态都不得误报。
        var absolute = new string(['@', '"', 'D', ':', '\\', 'h', 'o', 't', '"']);
        Assert.DoesNotMatch(DriveRelativeVerbatimLiteral, absolute);

        var relativeWithoutDrive = new string(['@', '"', 'E', 'x', 'i', 'f', 'T', 'o', 'o', 'l', '.', 'e', 'x', 'e', '"']);
        Assert.DoesNotMatch(DriveRelativeVerbatimLiteral, relativeWithoutDrive);
    }
}
