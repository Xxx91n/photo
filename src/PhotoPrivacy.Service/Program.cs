using Microsoft.Extensions.Hosting;
using PhotoPrivacy.Core.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "PhotoPrivacyCleaner";
});
builder.Services.AddHostedService<MetadataCleanerWorker>();

var app = builder.Build();
await app.RunAsync();
