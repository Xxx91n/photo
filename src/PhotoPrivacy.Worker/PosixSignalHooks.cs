using System.Runtime.InteropServices;

namespace PhotoPrivacy.Worker;

internal sealed class PosixSignalHooks : IDisposable
{
    private PosixSignalRegistration? _sigInt;
    private PosixSignalRegistration? _sigTerm;
    private PosixSignalRegistration? _sigQuit;

    private PosixSignalHooks()
    {
    }

    public static PosixSignalHooks Register(Action onSignal)
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

        _sigInt = null;
        _sigTerm = null;
        _sigQuit = null;
    }
}
