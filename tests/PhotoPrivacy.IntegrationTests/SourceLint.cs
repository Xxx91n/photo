using System.Text;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 架构恢复 — 源码文本检查共享 helper（唯一实现）。
/// 仓库根定位、源码读取与行注释剥除的唯一出处；
/// 各源码文本检查（PublishPropertiesCsprojGuardTests / SmokeScriptDiagnosticsTests 等）
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

    /// <summary>读取源码并整篇剥除 // 行注释（逐行剥除后拼接，与既有检查语义一致）。</summary>
    public static string ReadStripped(params string[] segments)
    {
        return string.Concat(ReadStrippedLines(segments));
    }

    /// <summary>
    /// （#85）同 <see cref="ReadStripped"/>，但保留换行——供需要判**顺序**或对样本做行级增删的
    /// 检查使用；读取与剥注释仍走本类单一实现，不在各 guard 里另起一套。
    /// </summary>
    public static string ReadStrippedPreservingLines(params string[] segments)
    {
        return string.Join("\n", ReadStrippedLines(segments));
    }

    private static string[] ReadStrippedLines(string[] segments)
    {
        return File.ReadAllLines(Path.Combine(new[] { RepoRoot }.Concat(segments).ToArray()), Encoding.UTF8)
            .Select(StripLineComment)
            .ToArray();
    }

    /// <summary>
    /// （#65）枚举 src/ 下全部手写 .cs（Core/Ipc/Worker 三工程），
    /// 排除 bin/obj/target 与生成代码 —— 全仓级形态检查的唯一枚举出处，不在各 guard 里重复。
    /// </summary>
    public static List<string> SrcCsFiles()
    {
        var srcRoot = Path.Combine(RepoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src root not found: {srcRoot}");
        return Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => IsNotBuildArtifact(RelativeToRoot(srcRoot, f)) &&
                        !f.EndsWith(".g.cs") && !f.EndsWith(".GlobalUsings.g.cs"))
            .ToList();
    }

    /// <summary>
    /// （#63）按仓库相对根名与通配枚举源码文件，排除 bin/obj/target 构建产物——
    /// 新检查的 tests/ .cs 与 ui/ .rs/.toml 等非 src 枚举走本方法（存量 guard 的
    /// 既有枚举为预存状态，不在本方法收编范围），与 <see cref="SrcCsFiles"/> 同一出处，
    /// 不在各 guard 里另起一套排除规则。
    /// </summary>
    public static List<string> EnumerateSourceFiles(string rootName, string pattern)
    {
        var root = Path.Combine(RepoRoot, rootName);
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.GetFiles(root, pattern, SearchOption.AllDirectories)
            .Where(f => IsNotBuildArtifact(RelativeToRoot(root, f)))
            .ToList();
    }

    /// <summary>
    /// （#63）对「带前导分隔符、相对各自扫描根」的路径判构建产物：排除 bin/obj/target 段。
    /// 输入规则由调用方保证——相对路径限定在扫描根之下，祖先目录撞名（仓库恰住在
    /// `~/bin/photo` 这类路径下）不会误杀全仓扫描；前导分隔符保证顶层 `bin/…` 也命中
    /// （裸相对路径 `bin/file.cs` 缺分隔前缀，不匹配段判定）。
    /// </summary>
    private static bool IsNotBuildArtifact(string pathRelativeToRoot) =>
        !pathRelativeToRoot.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
        !pathRelativeToRoot.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
        !pathRelativeToRoot.Contains(Path.DirectorySeparatorChar + "target" + Path.DirectorySeparatorChar);

    /// <summary>把扫描根下的绝对路径转成带前导分隔符的相对路径（谓词的输入规则）。</summary>
    private static string RelativeToRoot(string root, string path) =>
        Path.DirectorySeparatorChar + Path.GetRelativePath(root, path);

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
