using System.Runtime.InteropServices;

namespace PhotoPrivacy.Worker;

internal sealed class PosixSignalHooks : IDisposable
{
    private PosixSignalRegistration? _sigInt;
    private PosixSignalRegistration? _sigTerm;
    private PosixSignalRegistration? _sigQuit;
    private PosixSignalRegistration? _sigHup;

    private PosixSignalHooks()
    {
    }

    /// <summary>
    /// Register signal hooks. onSignal is called for SIGINT/SIGTERM/SIGQUIT.
    /// onReload is called for SIGHUP (ADR 0027: config reload).
    /// </summary>
    public static PosixSignalHooks Register(Action onSignal, Action? onReload = null)
    {
        var hooks = new PosixSignalHooks();

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return hooks;
        }

        try
        {
            hooks._sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => onSignal());
            hooks._sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => onSignal());
            hooks._sigQuit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, _ => onSignal());

            // ADR 0027: SIGHUP triggers config reload (same path as IPC ReloadConfig)
            if (onReload is not null)
            {
                hooks._sigHup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, _ => onReload());
            }
        }
        catch
        {
            hooks.Dispose();
        }

        return hooks;
    }

    public void Dispose()
    {
        _sigInt?.Dispose();
        _sigTerm?.Dispose();
        _sigQuit?.Dispose();
        _sigHup?.Dispose();

        _sigInt = null;
        _sigTerm = null;
        _sigQuit = null;
        _sigHup = null;
    }
}
