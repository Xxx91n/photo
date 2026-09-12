namespace PhotoPrivacy.Core.Runtime;

/// <summary>
/// ADR 0026 (Q7): Cross-platform single-instance guard abstraction.
/// Both Worker and UI processes use this to detect a duplicate instance.
/// Implementations: Mutex (Windows / .NET CoreCLR cross-process) and Flock (POSIX lockfile).
/// </summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>True if the calling process acquired the guard (is the unique owner).</summary>
    bool IsOwner { get; }
}
