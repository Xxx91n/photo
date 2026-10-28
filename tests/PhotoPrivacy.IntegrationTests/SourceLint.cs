using System.Text;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 架构恢复 — source-lint guard 共享 helper（唯一实现）。
/// 仓库根定位、源码读取与行注释剥除的唯一出处；
/// 各 source-lint guard（PublishPropertiesCsprojGuardTests / SmokeScriptDiagnosticsTests 等）
/// 统一改调本类，断言语义零变化。
/// 旧 Ui 工程退役（#61）后，Ui 源文件枚举与 ConfigPage 组合件读取随之移除。
/// </summary>
public static class SourceLint
{
    /// <summary>仓库根目录（自测试运行目录向上查 .git 或 PhotoPrivacy.sln 定位）。</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>剥除一行内的 // 行注释（含 /// doc 行），返回代码部分。</summary>
    public static string StripLineComment(string line)
    {
        var commentIdx = line.IndexOf("//");
        return commentIdx >= 0 ? line[..commentIdx] : line;
    }

    /// <summary>按仓库相对路径读取源码文件全文（UTF-8）。</summary>
    public static string Read(params string[] segments)
    {
        return File.ReadAllText(Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray()), Encoding.UTF8);
    }

    /// <summary>读取源码并整篇剥除 // 行注释（逐行剥除后拼接，与既有守卫语义一致）。</summary>
    public static string ReadStripped(params string[] segments)
    {
        return string.Concat(
            File.ReadAllLines(Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray()), Encoding.UTF8)
                .Select(StripLineComment));
    }

    /// <summary>
    /// （#65）枚举 src/ 下全部手写 .cs（Core/Ipc/Worker 三工程），
    /// 排除 bin/obj 与生成代码 —— 全仓级形态守卫的唯一枚举出处，不在各 guard 里重复。
    /// </summary>
    public static List<string> SrcCsFiles()
    {
        var srcRoot = Path.Combine(RepoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src root not found: {srcRoot}");
        return Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.EndsWith(".g.cs") && !f.EndsWith(".GlobalUsings.g.cs"))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        var current = dir;
        for (var i = 0; i < 8; i++)
        {
            current = Path.GetFullPath(Path.Combine(current, ".."));
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "PhotoPrivacy.sln")))
            {
                return current;
            }
        }

        throw new InvalidOperationException("repo root not found from " + dir);
    }
}
