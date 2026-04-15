using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

public sealed class ExifToolCommandBuilderTests
{
    [Fact]
    public void BuildStartArguments_Should_Contain_StayOpen_And_Api_Flags()
    {
        var args = ExifToolCommandBuilder.BuildStartArguments(AppConfig.Default);

        Assert.Contains("-stay_open", args);
        Assert.Contains("true", args);
        Assert.Contains("-@", args);
        Assert.Contains("-", args);
        Assert.Contains("-API", args);
        Assert.Contains("WindowsLongPath=1", args);
        Assert.Contains("LargeFileSupport=1", args);
    }

    [Fact]
    public void BuildWipeTaskBlock_Should_Contain_TaskDone_And_Execute()
    {
        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(@"D:\hot\a.jpg", "123");

        Assert.Contains("-all=", block, StringComparison.Ordinal);
        Assert.Contains("-overwrite_original", block, StringComparison.Ordinal);
        Assert.Contains("-echo1 TASK_DONE_123", block, StringComparison.Ordinal);
        Assert.Contains("-execute", block, StringComparison.Ordinal);
    }
}
