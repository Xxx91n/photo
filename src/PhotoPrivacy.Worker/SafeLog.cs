namespace PhotoPrivacy.Worker;

/// <summary>
/// 日志失败只降级、不反噬：服务会话下 EventLog provider 写失败会让 MEL <c>Logger.Log</c> 直接抛
/// <see cref="AggregateException"/>（#85 实测：Cannot open log for source，Win32 1722）。
/// IPC 响应送达、连接循环与配置重载的语义都不得取决于日志 provider 是否健康。
/// </summary>
// Design(#85): 日志调用失败必须吞掉——响应是否送达不得取决于日志 provider 健康。
internal static class SafeLog
{
    internal static void Try(Action log)
    {
        try
        {
            log();
        }
        catch
        {
            // best-effort：日志写失败静默丢弃，不打断调用方语义。
        }
    }
}
