namespace PhotoPrivacy.Core.Pipeline;

public static class ProcessedKeyBuilder
{
    /// <summary>构建去重键：#133 路线 2 起收敛为规范化路径（粗粒度命中），不再携带长度/时间戳指纹。</summary>
    public static string Build(string path)
    {
        return Path.GetFullPath(path);
    }
}
