using System.Text.Json;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

[Trait("Category", "ExifTool")]
public sealed class RealExifToolProbeTests : IntegrationTestBase
{
    [Fact]
    public async Task WipeMetadataAsync_WithRealExifTool_ShouldNotReturnSkipped_NoMetadata()
    {
        var exifToolPath = RequireExifTool();

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

        string? workDir = null;
        try
        {
            workDir = RealExifToolFixture.CreateWorkDirectory("realprobe19");
            var targetPath = await RealExifToolFixture.CreateTaggedJpegAsync(exifToolPath, workDir);

            await bridge.StartAsync(CancellationToken.None);
            Console.WriteLine($"ExifTool version: {bridge.VersionText}");

            var result = await bridge.WipeMetadataAsync(targetPath, CancellationToken.None);
            Console.WriteLine($"WipeResult: {result}");

            Assert.Equal(WipeResult.Cleaned_Modified, result);

            var remainingMake = await RealExifToolFixture.ReadMakeAsync(exifToolPath, targetPath);
            Assert.True(
                string.IsNullOrWhiteSpace(remainingMake),
                $"擦除后 EXIF:Make 还留着：{remainingMake}");
        }
        finally
        {
            try
            {
                await bridge.StopAsync(CancellationToken.None);
            }
            finally
            {
                if (workDir is not null)
                {
                    RealExifToolFixture.TryDelete(workDir);
                }
            }
        }
    }

    /// <summary>
    /// #86 可重复探针（AC3）：真实 ExifTool 上版本必须解析成功并上报。
    ///
    /// 不依赖机器上的现成照片——只走状态查询驱动的版本解析路径，
    /// 任何装了 ExifTool 的机器都能跑。
    /// 注意：`Category=ExifTool` 被 ci.yml 与 release.yml 的默认过滤排除，本用例不会自动执行；
    /// 需要人工在装有 ExifTool 的机器上显式跑：
    /// <c>dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"</c>。
    /// </summary>
    [Fact]
    public async Task EnsureVersionAsync_WithRealExifTool_Should_Report_Parsed_Version()
    {
        var exifToolPath = RequireExifTool();

        var config = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                Path = exifToolPath
            }
        };

        var bridge = new ExifToolBridge(new ProcessExifToolProcess(), config);

        try
        {
            // 不预先 StartAsync：就是要走「桥尚未启动 → 版本解析冷启动」这条 #86 路径。
            await bridge.EnsureVersionAsync(CancellationToken.None);

            Console.WriteLine($"ExifTool version: {bridge.VersionText}");
            Assert.NotEqual(ExifToolVersionText.Unknown, bridge.VersionText);
            Assert.Matches(@"\d+(\.\d+)+", bridge.VersionText);
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
        }
    }
}
