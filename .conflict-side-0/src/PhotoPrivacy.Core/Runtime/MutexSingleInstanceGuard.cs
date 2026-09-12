using System.Threading;

namespace PhotoPrivacy.Core.Runtime;

/// <summary>
/// ADR 0026 (Q7): Single-instance guard using a named system Mutex.
/// .NET CoreCLR provides cross-process named Mutex on Windows, Linux, and macOS.
/// Recovers from an abandoned mutex (previous owner crashed without releasing).
/// </summary>
public sealed class MutexSingleInstanceGuard : ISingleInstanceGuard
{
    private readonly Mutex _mutex;
    private readonly bool _isOwner;

    public MutexSingleInstanceGuard(string mutexName)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName, out var isOwner);
            _isOwner = isOwner;
        }
        catch (AbandonedMutexException)
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName, out _);
            _isOwner = true;
        }
    }

    public bool IsOwner => _isOwner;

    public void Dispose()
    {
        try { if (_isOwner) _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}
