using System.Text.Json;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.Smoke;

[Trait("Category", "ExifTool")]
public sealed class RealExifToolProbeTest : IntegrationTestBase
{
    [Fact]
    public async Task WipeMetadataAsync_WithRealExifTool_ShouldNotReturnSkipped_NoMetadata()
    {
        var exifToolPath = RequireExifTool();
        var targetPath = @"D:\hot\1.png";

        if (!File.Exists(targetPath))
        {
            // Skip if test file doesn't exist
            return;
        }

        var config = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                Path = exifToolPath
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<ExifToolBridge>();

        var process = new ProcessExifToolProcess();
        var bridge = new ExifToolBridge(process, config, logger);

        try
        {
            await bridge.StartAsync(CancellationToken.None);
            Console.WriteLine($"ExifTool version: {bridge.VersionText}");

            var result = await bridge.WipeMetadataAsync(targetPath, CancellationToken.None);
            Console.WriteLine($"WipeResult: {result}");

            Assert.NotEqual(WipeResult.Skipped_NoMetadata, result);
            Console.WriteLine($"PASS: Result was {result} (not Skipped_NoMetadata)");
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }
}
