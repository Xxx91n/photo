namespace PhotoPrivacy.Core.Pipeline;

public sealed class LocalFileOperations : IFileOperations
{
    private readonly Action<string, string> _move;
    private readonly Action<string, string, bool> _copy;
    private readonly Action<string> _delete;
    private readonly Action<string> _ensureDirectory;

    public LocalFileOperations(
        Action<string, string>? move = null,
        Action<string, string, bool>? copy = null,
        Action<string>? delete = null,
        Action<string>? ensureDirectory = null)
    {
        _move = move ?? File.Move;
        _copy = copy ?? File.Copy;
        _delete = delete ?? File.Delete;
        _ensureDirectory = ensureDirectory ?? (path => Directory.CreateDirectory(path));
    }

    public void EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _ensureDirectory(path);
    }

    public void Move(string source, string destination)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);

        try
        {
            if (File.Exists(destination))
            {
                _delete(destination);
            }

            _move(source, destination);
        }
        catch (IOException)
        {
            _copy(source, destination, true);
            _delete(source);
        }
    }

    public void Copy(string source, string destination, bool overwrite)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        _copy(source, destination, overwrite);
    }
    public void AtomicCopy(string source, string destination, bool overwrite)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);

        // Random suffix on temp to prevent concurrent collisions
        var tempPath = destination + ".tmp." + Path.GetRandomFileName();

        _copy(source, tempPath, true);

        if (overwrite && File.Exists(destination))
        {
            // File.Replace is cross-platform atomic (Windows ReplaceFile, Unix rename(2))
            File.Replace(tempPath, destination, null, true);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

    public async Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        const int bufferSize = 65536;
        using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous);
        using var dst = new FileStream(destination, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
        await src.CopyToAsync(dst, bufferSize, cancellationToken).ConfigureAwait(false);
        await dst.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AtomicCopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
    {
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        var tempPath = destination + ".tmp." + Path.GetRandomFileName();
        await CopyAsync(source, tempPath, overwrite: true, cancellationToken).ConfigureAwait(false);
        // File.Move is metadata-only; wrapping in Task.CompletedTask for interface uniformity
        if (overwrite && File.Exists(destination))
        {
            File.Replace(tempPath, destination, null, true);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

    public Task MoveAsync(string source, string destination, CancellationToken cancellationToken)
    {
        // Move is metadata-only (atomic on same volume), not I/O bound — wrap sync
        EnsureDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
        Move(source, destination);
        return Task.CompletedTask;
    }

}
