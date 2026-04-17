using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Cli;
using PhotoPrivacy.Core.Runtime;
using PhotoPrivacy.Core.Worker;

var builder = Host.CreateApplicationBuilder(args);
var mode = RuntimeModeResolver.Resolve(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

if (mode == RuntimeMode.Service)
{
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "PhotoPrivacyCleaner";
    });
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
    ApplicationConfiguration.Initialize();
    var runtimeControl = app.Services.GetRequiredService<IRuntimeControl>();
    Application.Run(new TrayApplicationContext(app, runtimeControl));
    return;
}

await app.RunAsync();
