namespace PhotoPrivacy.Core.Watcher;

public interface IFolderWatcher
{
    void Start();

    void Stop();
}

public interface IRecoveryScanner
{
    IReadOnlyList<string> ScanAll(string rootPath);
}
