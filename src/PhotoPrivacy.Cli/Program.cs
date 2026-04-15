using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PhotoPrivacy.Core.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<MetadataCleanerWorker>();

var app = builder.Build();
await app.RunAsync();
