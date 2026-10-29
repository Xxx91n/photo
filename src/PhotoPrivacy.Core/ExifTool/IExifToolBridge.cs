namespace PhotoPrivacy.Core.ExifTool;

public interface IExifToolBridge
{
    string VersionText { get; }

    /// <summary>
    /// 解析 ExifTool 版本（#86）：尚未启动则冷启动（启动即探 -ver），已启动但版本仍未知则补探，
    /// 已解析出真实版本则立即返回。<b>不进入文件处理热路径</b>——只有状态消费者显式请求版本时才调用。
    /// </summary>
    Task EnsureVersionAsync(CancellationToken cancellationToken);

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken);
}

public interface IExifToolProcess
{
    event Action<string>? StdoutLine;

    bool IsRunning { get; }

    string? LastStderrLine { get; }

    Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken);

    Task WriteStdinAsync(string text, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
