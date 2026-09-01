using System.Text;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// B06 / 架构恢复票 07 — source-lint guard 共享 helper（唯一实现）。
/// 仓库根定位、源码读取、行注释剥除与 Ui 源文件枚举的唯一出处；
/// 各 source-lint guard（UiLauncherSourceTests / DesignSystemTests /
/// WorkerProcessManagerTests / HardcodedChineseScanTests 等）统一改调本类，断言语义零变化。
/// </summary>
public static class SourceLint
{
    /// <summary>仓库根目录（自测试运行目录向上查 .git 或 PhotoPrivacy.sln 定位）。</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>src/PhotoPrivacy.Ui 源码根。</summary>
    public static string UiSourceRoot { get; } = Path.Combine(RepoRoot, "src", "PhotoPrivacy.Ui");

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

    /// <summary>枚举 src/PhotoPrivacy.Ui 全部手写 .cs（排除 bin/obj 与生成代码）。</summary>
    public static List<string> UiCsFiles()
    {
        Assert.True(Directory.Exists(UiSourceRoot), $"Ui source root not found: {UiSourceRoot}");
        return Directory.GetFiles(UiSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.EndsWith(".g.cs") && !f.EndsWith(".GlobalUsings.g.cs"))
            .ToList();
    }

    /// <summary>枚举 src/PhotoPrivacy.Ui 全部 .axaml（排除 bin/obj）。</summary>
    public static List<string> UiAxamlFiles()
    {
        Assert.True(Directory.Exists(UiSourceRoot), $"Ui source root not found: {UiSourceRoot}");
        return Directory.GetFiles(UiSourceRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
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
