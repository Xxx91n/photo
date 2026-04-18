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

const string MutexName = @"Global\PhotoPrivacyCleaner_SingleInstance";
using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isNewInstance);
if (!isNewInstance)
{
    var requestedMode = RuntimeModeResolver.ResolveFromArgs(args);

#if WINDOWS
    if (requestedMode == RuntimeMode.Background)
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

if (mode == RuntimeMode.Background)
{
#if WINDOWS
    ApplicationConfiguration.Initialize();
    var runtimeControl = app.Services.GetRequiredService<IRuntimeControl>();
    Application.Run(new TrayApplicationContext(app, runtimeControl));
    return Environment.ExitCode;
#else
    Console.Error.WriteLine("[PhotoPrivacy] background 模式仅支持 Windows。请改用 --mode cli。");
    return 1;
#endif
}

await app.RunAsync();
return Environment.ExitCode;
