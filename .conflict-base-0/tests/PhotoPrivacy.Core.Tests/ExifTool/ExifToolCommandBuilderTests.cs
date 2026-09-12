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
        Assert.Contains("True", args);
        Assert.Contains("-@", args);
        Assert.Contains("-", args);
        Assert.Contains("-API", args);
        Assert.Contains("WindowsLongPath=1", args);
        Assert.Contains("LargeFileSupport=1", args);
    }

    [Fact]
    public void BuildStartArguments_Should_Start_With_StayOpen_And_Stdin_Stream_Args()
    {
        var args = ExifToolCommandBuilder.BuildStartArguments(AppConfig.Default);

        Assert.True(args.Length >= 4);
        Assert.Equal("-stay_open", args[0]);
        Assert.Equal("True", args[1]);
        Assert.Equal("-@", args[2]);
        Assert.Equal("-", args[3]);
    }

    [Fact]
    public void BuildWipeTaskBlock_Should_Contain_TaskDone_And_Execute()
    {
        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(@"D:\hot\a.jpg", "123");

        Assert.Contains("-all=", block, StringComparison.Ordinal);
        Assert.Contains("-overwrite_original", block, StringComparison.Ordinal);
        Assert.Contains("-echo1\nTASK_DONE_123", block, StringComparison.Ordinal);
        Assert.Contains("-execute", block, StringComparison.Ordinal);
    }
}
