using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

// Extend shutdown timeout so StopAsync has time to flush the audit log
// and drain the ExifTool bridge cleanly before the host force-kills the app.
// The default 5 s was too short when StopAsync received an already-cancelled
// token and the bridge needed a moment to tear down the stay_open process.
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(10);
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
});

builder.Services.AddHostedService<MetadataCleanerWorker>();

var app = builder.Build();
await app.RunAsync();
