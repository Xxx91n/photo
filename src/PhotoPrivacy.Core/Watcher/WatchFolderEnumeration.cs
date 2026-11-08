namespace PhotoPrivacy.Core.Watcher;

/// <summary>
/// 监控范围文件枚举的唯一入口：深度只由 <c>include_subdirectories</c> 决定，与 FSW 的监听深度
/// （<see cref="FswFolderWatcher"/>）同一把尺。规则引擎不按目录深度过滤，枚举越界就是把监听范围
/// 之外的照片真实改写元数据。启动首扫、重载首扫、重载拆卸快照、轮询兜底快照与 FSW 恢复补扫
/// 都从这里取深度，不得再各写一份 <c>SearchOption</c>。
/// </summary>
// Design(#244): 监控范围枚举全仓唯一入口，深度只由 include_subdirectories 决定；不得再各写一份 SearchOption（#109 修复后定稿，残余见 #82 域内）。
public static class WatchFolderEnumeration
{
    /// <summary>
    /// 按监控深度枚举 rootPath 下的文件。目录不存在时返回空序列（首扫、快照与恢复补扫的既有口径：
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
