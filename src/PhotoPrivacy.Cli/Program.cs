using System.Threading;
using Microsoft.Extensions.DependencyInjection;
#if WINDOWS
using System.Windows.Forms;
#endif
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Cli;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;

ConsoleCancelEventHandler? cancelKeyHandler = null;
EventHandler? processExitHandler = null;
PosixSignalHooks? posixHooks = null;
#if WINDOWS
NativeConsoleControlHandler? nativeHandler = null;
#endif

const string MutexName = @"Global\PhotoPrivacyCleaner_SingleInstance";
using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isNewInstance);
if (!isNewInstance)
{
    var requestedMode = RuntimeModeResolver.ResolveFromArgs(args);
    if (!RuntimeModeResolver.HasModeOption(args) && Environment.UserInteractive)
    {
        requestedMode = RuntimeMode.Background;
    }

#if WINDOWS
    if (InstanceConflictUiPolicy.ShouldShowInteractivePrompt(requestedMode, Environment.UserInteractive))
    {
        MessageBox.Show(
            "另一个实例已在运行，已拒绝重复启动。",
            "PhotoPrivacy",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
    else
#endif
    {
        Console.Error.WriteLine("[PhotoPrivacy] 另一个实例已在运行，退出。");
        Console.Error.WriteLine("[PhotoPrivacy] another instance is already in use.");
    }

    await InstanceConflictAudit.TryWriteAsync(args, requestedMode, MutexName);
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
var mode = RuntimeModeResolver.Resolve(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

if (mode == RuntimeMode.Service)
{
#if WINDOWS
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "PhotoPrivacyCleaner";
    });
#else
    Console.Error.WriteLine("[PhotoPrivacy] service 模式仅支持 Windows。请改用 --mode cli。");
    return 1;
#endif
}

// Extend shutdown timeout so StopAsync has time to flush the audit log
// and drain the ExifTool bridge cleanly before the host force-kills the app.
// The default 5 s was too short when StopAsync received an already-cancelled
// token and the bridge needed a moment to tear down the stay_open process.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddSingleton<IRuntimeControl, RuntimeControl>();
builder.Services.AddHostedService<MetadataCleanerWorker>();

var app = builder.Build();

var shutdownCoordinator = new ShutdownCoordinator(
    stopToken => app.StopAsync(stopToken),
    stopTimeout: TimeSpan.FromSeconds(4));
posixHooks = PosixSignalHooks.Register(() => shutdownCoordinator.RequestStop());
RegisterBestEffortShutdown(shutdownCoordinator, ref cancelKeyHandler, ref processExitHandler
#if WINDOWS
    , ref nativeHandler
#endif
);

if (mode == RuntimeMode.Background)
{
#if WINDOWS
    ApplicationConfiguration.Initialize();
    var runtimeControl = app.Services.GetRequiredService<IRuntimeControl>();
    Application.Run(new TrayApplicationContext(app, runtimeControl));
    UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler
#if WINDOWS
        , nativeHandler
#endif
    );
    posixHooks.Dispose();
    return Environment.ExitCode;
#else
    Console.Error.WriteLine("[PhotoPrivacy] background 模式仅支持 Windows。请改用 --mode cli。");
    return 1;
#endif
}

await app.RunAsync();
UnregisterBestEffortShutdown(cancelKeyHandler, processExitHandler
#if WINDOWS
    , nativeHandler
#endif
);
posixHooks.Dispose();
return Environment.ExitCode;

static void RegisterBestEffortShutdown(
    ShutdownCoordinator coordinator,
    ref ConsoleCancelEventHandler? cancelKeyHandler,
    ref EventHandler? processExitHandler
#if WINDOWS
    , ref NativeConsoleControlHandler? nativeHandler
#endif
    )
{
    cancelKeyHandler = (_, e) =>
    {
        e.Cancel = true;
        coordinator.RequestStop();
    };
    Console.CancelKeyPress += cancelKeyHandler;

    processExitHandler = (_, _) => coordinator.RequestStop();
    AppDomain.CurrentDomain.ProcessExit += processExitHandler;

#if WINDOWS
    nativeHandler = controlType =>
    {
        if (controlType is NativeConsoleControlType.CtrlCloseEvent
            or NativeConsoleControlType.CtrlLogoffEvent
            or NativeConsoleControlType.CtrlShutdownEvent)
        {
            coordinator.RequestStop();
            return true;
        }

        return false;
    };

    NativeConsole.SetConsoleCtrlHandler(nativeHandler, add: true);
#endif
}

static void UnregisterBestEffortShutdown(
    ConsoleCancelEventHandler? cancelKeyHandler,
    EventHandler? processExitHandler
#if WINDOWS
    , NativeConsoleControlHandler? nativeHandler
#endif
    )
{
    if (cancelKeyHandler is not null)
    {
        Console.CancelKeyPress -= cancelKeyHandler;
    }

    if (processExitHandler is not null)
    {
        AppDomain.CurrentDomain.ProcessExit -= processExitHandler;
    }

#if WINDOWS
    if (nativeHandler is not null)
    {
        NativeConsole.SetConsoleCtrlHandler(nativeHandler, add: false);
    }
#endif
}

#if WINDOWS
internal enum NativeConsoleControlType : uint
{
    CtrlCEvent = 0,
    CtrlBreakEvent = 1,
    CtrlCloseEvent = 2,
    CtrlLogoffEvent = 5,
    CtrlShutdownEvent = 6
}

internal delegate bool NativeConsoleControlHandler(NativeConsoleControlType controlType);

internal static partial class NativeConsole
{
    [System.Runtime.InteropServices.DllImport("Kernel32")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool SetConsoleCtrlHandler(NativeConsoleControlHandler handler, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool add);
}
#endif
