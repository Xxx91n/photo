using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 票01 source-lint 守卫（B02 L1）：全仓 src+tests 禁止无超时形态的进程等待与按进程名全系统匹配杀。
/// 整改范式：WaitForExitAsync 必须传入来自超时 CTS 的 token（见 DryRunOutputFlowTests.WaitForExitWithTimeoutAsync）；
/// 清理自身启动的进程一律 Kill(entireProcessTree: true)，禁止按进程名全系统匹配。
/// </summary>
public sealed class ProcessHygieneGuardTests
{
    [Fact]
    public void WaitForExitAsync_Should_Never_Be_Parameterless()
    {
        var violations = ScanSourceLines(@"\.WaitForExitAsync\s*\(\s*\)");
        Assert.True(violations.Count == 0,
            "无超时形态的 WaitForExitAsync 调用必须清零（传 CTS token + 超时后 tree-kill）：\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Processes_Should_Never_Be_Killed_By_Name()
    {
        var violations = ScanSourceLines(@"\." + "GetProcesses" + "ByName" + @"s*\(");
        Assert.True(violations.Count == 0,
            "禁止按进程名全系统匹配杀进程（只许杀自启进程树 Kill(entireProcessTree: true)）：\n" + string.Join("\n", violations));
    }

    private static List<string> ScanSourceLines(string pattern)
    {
        var repoRoot = SourceLint.RepoRoot;
        var regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
        var violations = new List<string>();

        foreach (var dir in new[] { "src", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, dir), "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/bin/") || normalized.Contains("/obj/"))
                {
                    continue;
                }

                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//"))
                    {
                        continue;
                    }

                    if (regex.IsMatch(lines[i]))
                    {
                        violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{i + 1}");
                    }
                }
            }
        }

        return violations;
    }

}
