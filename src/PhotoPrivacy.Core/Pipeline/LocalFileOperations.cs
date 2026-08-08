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

        var tempPath = destination + ".tmp";

        _copy(source, tempPath, true);

        if (overwrite && File.Exists(destination))
        {
            File.Move(tempPath, destination, overwrite: true);
        }
        else
        {
            File.Move(tempPath, destination);
        }
    }

}
