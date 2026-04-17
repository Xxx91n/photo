using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Tests.Configuration;

public sealed class AppConfigJsonTests
{
    [Fact]
    public void ToIndentedJson_Should_Emit_SnakeCase_Fields()
    {
        var cfg = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                DryRun = true,
                ExtraExifToolArgs = ["-charset", "filename=utf8"]
            },
            Watch = AppConfig.Default.Watch with
            {
                HotFolder = @"D:\override\hot"
            }
        };

        var json = AppConfigJson.ToIndentedJson(cfg);

        Assert.Contains("\"schema_version\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"dry_run\": true", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hot_folder\": \"D:\\\\override\\\\hot\"", json, StringComparison.Ordinal);
        Assert.Contains("\"extra_exiftool_args\": [", json, StringComparison.Ordinal);
    }
}
