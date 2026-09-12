namespace PhotoPrivacy.Core.Watcher;

public interface IRecoveryScanner
{
    IReadOnlyList<string> ScanAll(string rootPath);
}

public interface IFileSystemWatcherFactory
{
    FileSystemWatcher Create(string path);
}
