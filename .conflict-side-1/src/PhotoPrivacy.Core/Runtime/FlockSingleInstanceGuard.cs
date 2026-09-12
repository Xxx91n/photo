using System.IO;

namespace PhotoPrivacy.Core.Runtime;

/// <summary>
/// ADR 0026 (Q7): POSIX single-instance guard using an exclusively-opened lockfile.
/// Uses FileStream with FileShare.None which maps to exclusive open on POSIX
/// (effectively flock-style). POSIX-native (no dependency on named Mutex availability).
/// </summary>
public sealed class FlockSingleInstanceGuard : ISingleInstanceGuard
{
    private readonly FileStream? _lockStream;
    private readonly bool _isOwner;
    private readonly string _lockPath;

    public FlockSingleInstanceGuard(string lockPath)
    {
        _lockPath = lockPath;
        var dir = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            // FileShare.None makes the open exclusive: a second process cannot open it, so IsOwner=false.
            _lockStream = new FileStream(lockPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            _isOwner = true;
        }
        catch (IOException)
        {
            _isOwner = false;
        }
    }

    public bool IsOwner => _isOwner;

    public void Dispose()
    {
        try { _lockStream?.Dispose(); } catch { }
        if (_isOwner)
        {
            try { File.Delete(_lockPath); } catch { /* best effort */ }
        }
    }
}
