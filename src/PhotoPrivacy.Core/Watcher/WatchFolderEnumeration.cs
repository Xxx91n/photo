namespace PhotoPrivacy.Core.Watcher;

/// <summary>
/// 监控范围文件枚举的唯一入口：深度只由 <c>include_subdirectories</c> 决定，与 FSW 的监听深度
/// （<see cref="FswFolderWatcher"/>）同一把尺。规则引擎不按目录深度过滤，枚举越界就是把监听范围
/// 之外的照片真实改写元数据。启动先扫一遍、重载扫描、重载拆卸快照、轮询补漏扫快照与 FSW 恢复补漏扫
/// 都从这里取深度，不得再各写一份 <c>SearchOption</c>。
/// </summary>
public static class WatchFolderEnumeration
{
    /// <summary>
    /// 按监控深度枚举 rootPath 下的文件。目录不存在时返回空序列（启动先扫一遍、快照与恢复补漏扫的既有规则：
    /// 没有目录就没有东西可报，而不是抛错）。
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string rootPath, bool includeSubdirectories)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", searchOption))
        {
            yield return path;
        }
    }
}
