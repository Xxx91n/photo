using System.Runtime.InteropServices;

namespace PhotoPrivacy.Core.Constants;

/// <summary>
/// Cross-platform locale file path resolver.
/// Three layers (ascending priority, later overrides earlier on same key):
///   1. Embedded resource (DLL internal, always available)
///   2. exe-same-dir Localization/Locales/ (portable + installed)
///   3. User config dir locales/ (per-user override, normal permissions)
/// </summary>
public static class LocalePathResolver
{
    public static string ExeLocaleDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Localization", "Locales");

    public static string UserLocaleDirectory =>
        Path.Combine(GetUserAppDataDir(), "locales");

    /// <summary>
    /// Cross-platform user application data directory for PhotoPrivacy.
    /// Windows: %LOCALAPPDATA%PhotoPrivacy
    /// Linux:   $XDG_DATA_HOME or ~/.local/share/PhotoPrivacy
    /// macOS:   ~/Library/Application Support/PhotoPrivacy (via SpecialFolder.UserProfile)
    /// </summary>
    public static string GetUserAppDataDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "PhotoPrivacy");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "PhotoPrivacy");
        }

        // Linux: XDG_DATA_HOME takes priority, fallback to ~/.local/share
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "PhotoPrivacy");
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userHome, ".local", "share", "PhotoPrivacy");
    }
}
