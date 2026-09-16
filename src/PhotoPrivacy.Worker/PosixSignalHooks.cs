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
    /// onReload is called for SIGHUP (ADR 0027: config reload) — async 委托；
    /// 票 06（T1 承接 / 票 05 A-005 同族）：信号回调线程不得 sync-over-async，
    /// reload 由本层 Task.Run 编组到线程池并立即返回；重入串行化由
    /// MetadataCleanerWorker._reloadGate 兜底，异常由调用方在 onReload 内部记录。
    /// </summary>
    public static PosixSignalHooks Register(Action onSignal, Func<Task>? onReload = null)
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
            // 票 06（T1）：回调线程不阻塞——Task.Run 编组 fire-and-forget
            if (onReload is not null)
            {
                hooks._sigHup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, sigCtx => { _ = Task.Run(onReload); });
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
