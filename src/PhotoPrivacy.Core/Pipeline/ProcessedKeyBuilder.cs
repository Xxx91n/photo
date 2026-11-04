namespace PhotoPrivacy.Core.Pipeline;

public static class ProcessedKeyBuilder
{
    /// <summary>
    /// 去重键的比较规则：Windows 文件系统大小写不敏感，Unix 类（含 macOS）用 Ordinal 区分大小写。
    /// Unix 卷实际大小写不敏感时，大小写变体只会「未命中 → 重新处理」，不会被误判为已处理；
    /// 隐私安全侧宁可多处理，不可漏处理。
    /// </summary>
    public static StringComparer KeyComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>构建去重键：#133 路线 2 起收敛为规范化路径（粗粒度命中），不再携带长度/时间戳指纹。</summary>
    public static string Build(string path)
    {
        return Path.GetFullPath(path);
    }
}
