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
        // 票 28 返修：BuildWipeTaskBlock 校验绝对路径，Windows 风格字面量在 Linux 非法——用平台绝对路径
        var target = Path.Combine(Path.GetTempPath(), "pp-cb", "a.jpg");
        var block = ExifToolCommandBuilder.BuildWipeTaskBlock(target, "123");

        Assert.Contains("-all=", block, StringComparison.Ordinal);
        Assert.Contains("-overwrite_original", block, StringComparison.Ordinal);
        Assert.Contains("-echo1\nTASK_DONE_123", block, StringComparison.Ordinal);
        Assert.Contains("-execute", block, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWipeTaskBlock_Should_Throw_For_Unmapped_Format()
    {
        // 票 01（A-001）：skip 情形不得产出不可完成的块（旧行为：回显 SKIP_ 而调用方等 TASK_DONE_ → 挂死）。
        var target = Path.Combine(Path.GetTempPath(), "pp-cb", "a.webp");

        var ex = Assert.Throws<InvalidOperationException>(
            () => ExifToolCommandBuilder.BuildWipeTaskBlock(target, "123"));

        Assert.Contains("unknown_format", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWipeTaskBlock_Should_Throw_When_Rules_Produce_No_Executable_Args()
    {
        var target = Path.Combine(Path.GetTempPath(), "pp-cb", "a.jpg");
        var rules = new Dictionary<string, bool>
        {
            [FormatRulesStore.Key("jpeg", "strip_all")] = false,
            [FormatRulesStore.Key("jpeg", "preserve_icc")] = true,
            [FormatRulesStore.Key("jpeg", "strip_exif")] = false,
            [FormatRulesStore.Key("jpeg", "strip_xmp")] = false,
            [FormatRulesStore.Key("jpeg", "strip_iptc")] = false,
            [FormatRulesStore.Key("jpeg", "strip_time")] = false
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => ExifToolCommandBuilder.BuildWipeTaskBlock(target, "123", rules));

        Assert.Contains("no_rules", ex.Message, StringComparison.Ordinal);
    }
}
