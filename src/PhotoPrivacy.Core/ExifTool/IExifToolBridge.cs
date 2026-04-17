namespace PhotoPrivacy.Core.ExifTool;

public interface IExifToolBridge
{
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task WipeMetadataAsync(string targetPath, CancellationToken cancellationToken);
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
