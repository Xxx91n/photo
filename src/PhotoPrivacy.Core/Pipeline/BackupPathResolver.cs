using System.Security.Cryptography;
using System.Text;

namespace PhotoPrivacy.Core.Pipeline;

/// <summary>
/// Single source of truth for backup path resolution.
///
/// 票 03（A-006 / D-006）：备份布局由「扁平文件名 + 后缀」改为「镜像相对路径布局」——
/// 备份根下按源文件相对其监控根的子路径镜像目录结构，结构性消除跨子目录同名文件的
/// 备份互相覆盖（原 bug：<c>hot/vacation/IMG_0001.jpg</c> 与 <c>hot/work/IMG_0001.jpg</c>
/// 都解析到同一个 <c>{backupDir}/IMG_0001.jpg.bak</c>，后写静默覆盖前写）。
///
/// 冲突规则（D-006 atomcode 组合方案）：目标槽位存在时——内容相同→跳过（幂等）；
/// 内容不同→写 <c>名字.时间戳.扩展名</c> 旁路版本；绝不静默覆盖。
/// 「同文件重处理保留最新」的合法语义由「同槽位+内容相同跳过」承接。
///
/// 边缘兜底：源路径无法归位监控子树时回退「文件名 + 路径短哈希」；
/// 防穿越复用与 <see cref="PhotoPrivacy.Core.Watcher.WatchPathFilter"/> 一致的
/// 组件级边界判定（GetRelativePath + ".." 检测，非裸字符串 startswith）；
/// 符号链接/ReparsePoint 拒绝；Windows 结尾点/空格/保留名规范化。
/// </summary>
public static class BackupPathResolver
{
    public const string DefaultBackupDirName = "bak";

    /// <summary>
    /// 兜底子目录名：源路径无法归位监控子树时落此目录，文件名保留原名 + 短哈希。
    /// 对应 atomcode 调研的 "_unsorted / lost+found" 旁路区（Borg "skip-with-warning" 哲学）。
    /// </summary>
    public const string FallbackDirName = "_unsorted";

    private static readonly char[] DirectorySeparators =
        { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    // Windows 设备保留名（不含扩展名前缀也算保留，如 CON.foo 仍非法）。
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string ResolveDefaultBackupDir(string hotFolder) =>
        Path.Combine(hotFolder, DefaultBackupDirName);

    /// <summary>
    /// 解析备份槽位路径：备份根下按源文件相对监控根的子路径镜像。
    /// 返回 null 表示 sourcePath 为空等无法解析情形（调用方按原逻辑不创建备份）。
    /// </summary>
    /// <param name="sourcePath">源文件完整路径。</param>
    /// <param name="hotFolder">监控根（Watch.HotFolder）。</param>
    /// <param name="backupDir">备份根（已解析的默认或配置目录）。</param>
    /// <param name="suffix">备份后缀（Backup.Suffix，如 ".bak"）。</param>
    public static string? ResolveBackupSlot(string sourcePath, string hotFolder, string backupDir, string suffix)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(backupDir))
        {
            return null;
        }

        // 规范化两侧再做边界判定（组件级 GetRelativePath + ".." 检测）。
        var normalizedSource = NormalizeFullPath(sourcePath);
        if (string.IsNullOrEmpty(normalizedSource))
        {
            return null;
        }

        var normalizedRoot = string.IsNullOrWhiteSpace(hotFolder) ? string.Empty : NormalizeFullPath(hotFolder);

        // 能归位监控子树 → 镜像相对路径；否则回退「文件名 + 路径短哈希」。
        // 边界判定复用 WatchPathFilter 的组件级实现（票 03 票面要求，防穿越单一来源）。
        string relativeSlot;
        if (!string.IsNullOrEmpty(normalizedRoot) && Watcher.WatchPathFilter.TryGetSubPathRelative(normalizedRoot, normalizedSource, out var relative))
        {
            relativeSlot = relative!;
        }
        else
        {
            var fileName = Path.GetFileName(normalizedSource);
            var hash = ShortPathHash(normalizedSource);
            relativeSlot = Path.Combine(FallbackDirName, fileName + "." + hash);
        }

        // 逐组件规范化（防结尾点/空格/保留名 + 防御性剥离任何残留的 ".." / 根前缀）。
        var safeRelative = SanitizeRelativePath(relativeSlot);
        if (string.IsNullOrEmpty(safeRelative))
        {
            safeRelative = Path.Combine(FallbackDirName, ShortPathHash(normalizedSource));
        }

        return Path.Combine(backupDir, safeRelative + suffix);
    }

    /// <summary>
    /// 旁路版本路径：槽位已存在且内容分歧时写 <c>槽位名.时间戳</c>（在 .bak 后缀之前插入）。
    /// 例：<c>IMG_0001.jpg.20260916-021530.bak</c>。同秒碰撞经 <paramref name="exists"/>
    /// 探测后追加 <c>-N</c> 序号区分。
    /// </summary>
    public static string ResolveSidecarPath(string slotPath, string suffix, DateTimeOffset timestamp, Func<string, bool>? exists)
    {
        var baseName = slotPath;
        if (!string.IsNullOrEmpty(suffix)
            && slotPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            baseName = slotPath[..^suffix.Length];
        }

        var stamp = timestamp.ToString("yyyyMMdd-HHmmss");
        var candidate = $"{baseName}.{stamp}{suffix}";
        if (exists is null || !exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; ; i++)
        {
            candidate = $"{baseName}.{stamp}-{i}{suffix}";
            if (!exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 逐字节内容比对：用于「槽位存在时内容相同→跳过 / 不同→旁路版本」判定。
    /// </summary>
    public static bool ContentsEqual(string pathA, string pathB)
    {
        try
        {
            var infoA = new FileInfo(pathA);
            var infoB = new FileInfo(pathB);
            if (!infoA.Exists || !infoB.Exists || infoA.Length != infoB.Length)
            {
                return false;
            }

            const int bufferSize = 81920;
            using var streamA = new FileStream(pathA, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            using var streamB = new FileStream(pathB, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
            var bufferA = new byte[bufferSize];
            var bufferB = new byte[bufferSize];
            while (true)
            {
                var readA = streamA.Read(bufferA, 0, bufferSize);
                var readB = streamB.Read(bufferB, 0, bufferSize);
                if (readA != readB)
                {
                    return false;
                }

                if (readA == 0)
                {
                    return true;
                }

                if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                {
                    return false;
                }
            }
        }
        catch
        {
            // 比对失败按"内容不同"处理——宁可落旁路版本也不冒静默覆盖险。
            return false;
        }
    }

    /// <summary>
    /// 符号链接 / ReparsePoint 检测：路径存在且为重解析点时返回 true（调用方拒绝处理）。
    /// 桌面单用户工具采用"检查时解析两侧"（atomcode 结论 4 / Borg create 语义）。
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 组件级边界判定：candidate 是否位于 root 子树内（含同路径）。
    /// 委托 <see cref="PhotoPrivacy.Core.Watcher.WatchPathFilter.IsSameOrSubPath"/>
    /// ——与监控排除判定同一实现（GetRelativePath + ".." 检测，非裸字符串 startswith）。
    /// 入参为绝对路径（相对输入由调用方先 Normalize）。
    /// </summary>
    public static bool IsSameOrUnderRoot(string root, string candidate)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return Watcher.WatchPathFilter.IsSameOrSubPath(candidate, root);
    }

    private static string NormalizeFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return fullPath.TrimEnd(DirectorySeparators);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 相对路径逐组件规范化：剥离 "." / ".." / 空组件 / 根前缀，
    /// 各组件做 Windows 结尾点/空格与保留名规范化。
    /// </summary>
    private static string SanitizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var parts = relativePath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
        var sanitized = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (string.Equals(part, ".", StringComparison.Ordinal)
                || string.Equals(part, "..", StringComparison.Ordinal))
            {
                continue;
            }

            var clean = SanitizeComponent(part);
            if (string.IsNullOrEmpty(clean))
            {
                continue;
            }

            sanitized.Add(clean);
        }

        return sanitized.Count == 0 ? string.Empty : Path.Combine(sanitized.ToArray());
    }

    /// <summary>
    /// 单组件规范化：剥离结尾点/空格（Win32 静默剥离导致碰撞判定错乱），
    /// 保留名前缀化，替换非法字符。返回规范化后组件；全被剥光返回 "_"。
    /// </summary>
    private static string SanitizeComponent(string component)
    {
        if (string.IsNullOrEmpty(component))
        {
            return string.Empty;
        }

        // Win32 会静默剥离结尾的 '.' 与 ' '——规范化以防同名碰撞判定错乱。
        var clean = component.TrimEnd('.', ' ');

        var builder = new StringBuilder(clean.Length);
        foreach (var ch in clean)
        {
            builder.Append(IsInvalidFileNameChar(ch) ? '_' : ch);
        }

        clean = builder.ToString();
        if (string.IsNullOrEmpty(clean))
        {
            clean = "_";
        }

        // 保留名（含其扩展名前缀，如 CON.foo）前缀化。
        var stem = clean;
        var dotIndex = clean.IndexOf('.');
        if (dotIndex > 0)
        {
            stem = clean[..dotIndex];
        }

        if (WindowsReservedNames.Contains(stem))
        {
            clean = "_" + clean;
        }

        return clean;
    }

    private static bool IsInvalidFileNameChar(char ch) =>
        ch is '<' or '>' or ':' or '"' or '|' or '?' or '*' || ch < ' ';

    /// <summary>路径短哈希：8 字符十六进制（SHA-256 前 4 字节），用于兜底消歧。</summary>
    private static string ShortPathHash(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        var builder = new StringBuilder(8);
        for (var i = 0; i < 4; i++)
        {
            builder.Append(bytes[i].ToString("x2"));
        }

        return builder.ToString();
    }
}
