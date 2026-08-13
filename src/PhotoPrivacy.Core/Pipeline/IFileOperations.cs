namespace PhotoPrivacy.Core.Pipeline;

public interface IFileOperations
{
    void EnsureDirectory(string path);

    void Move(string source, string destination);

    void Copy(string source, string destination, bool overwrite);

    void AtomicCopy(string source, string destination, bool overwrite);

    // Async versions (hot path uses these to avoid thread-pool blocking)
    Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken);

    Task AtomicCopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken);

    Task MoveAsync(string source, string destination, CancellationToken cancellationToken);
}
