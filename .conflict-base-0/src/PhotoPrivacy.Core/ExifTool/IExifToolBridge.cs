namespace PhotoPrivacy.Core.ExifTool;

public interface IExifToolBridge
{
    string VersionText { get; }

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
