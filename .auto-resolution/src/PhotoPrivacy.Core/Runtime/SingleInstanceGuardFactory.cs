using System.IO;
using System.Runtime.InteropServices;

namespace PhotoPrivacy.Core.Runtime;

/// <summary>
/// ADR 0026 (Q7): Factory that creates the platform-appropriate ISingleInstanceGuard.
/// Windows → MutexSingleInstanceGuard. POSIX (Linux/macOS) → FlockSingleInstanceGuard.
/// Callers pass a logical name (e.g. mutex name or per-user lock path base).
/// </summary>
public static class SingleInstanceGuardFactory
{
    /// <summary>For a given mutexName (Windows) or lockPathBase (POSIX), create the right guard.</summary>
    public static ISingleInstanceGuard Create(string name)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // ponytail: POSIX lockfile chosen for native reliability; Mutex is CoreCLR-abstracted
            // and would also work, but the POSIX fd-lock path avoids the NativeAOT named-Mutex caveat.
            var lockPath = Path.Combine(
                Path.GetTempPath(),
                "photoprivacy-" + SanitizeForPath(name) + ".lock");
            return new FlockSingleInstanceGuard(lockPath);
        }
        return new MutexSingleInstanceGuard(name);
    }

    private static string SanitizeForPath(string s)
    {
        // POSIX filenames cannot contain '/' — strip path separators from mutex-style names.
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c.ToString(), string.Empty);
        }
        // Drop Windows-style backslashes and leading "Global\" segment from mutex-style names
        // so the leftover name is a valid POSIX filename component.
        return s.Replace('\\', '-').TrimStart('-');
    }
}
