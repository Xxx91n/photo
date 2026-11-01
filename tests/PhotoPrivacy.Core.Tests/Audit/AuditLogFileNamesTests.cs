using PhotoPrivacy.Core.Audit;

namespace PhotoPrivacy.Core.Tests.Audit;

/// <summary>
/// 行为测试：审计文件名日期基准（#129）。统一基准的决策是「文件名按本地「今天」
/// 分卷，三处消费端（写入端、IPC 回填、UI tail）同口径」；本套用例钉死统一入口
/// <see cref="AuditLogFileNames.BaseName"/> 的取值规则，防止任一端重新引入
/// UTC 换算或独立格式化。
///
/// 防回潮形态：
/// ① 实现把「传入值即本地墙钟」误读成「再补一次 UTC 换算」——UTC+8 00:00–07:59
///    时窗内文件名会落到「昨天」，重演写入端/读取端分叉（#129 根因）；
/// ② 调用方绕过入口自行拼 <c>$"audit-{...:yyyy-MM-dd}.jsonl"</c>，三处口径各自漂移。
/// </summary>
public sealed class AuditLogFileNamesTests
{
    [Fact]
    public void BaseName_Should_Use_The_Given_Local_Date_As_Is()
    {
        // 传入值即本地墙钟：文件名字段只按 yyyy-MM-dd 格式化，不得再做任何时区换算。
        var localDay = new DateTime(2026, 10, 31, 23, 30, 0);
        Assert.Equal("audit-2026-10-31", AuditLogFileNames.BaseName(localDay));
    }

    [Fact]
    public void BaseName_Should_Never_Shift_Across_The_Local_Utc_Fork_Window()
    {
        // #129 实机根因时窗：UTC+8 00:00–07:59 时本地日期已进入「今天」而 UTC 仍是
        // 「昨天」。统一基准后文件名字段必须钉在本地日期——该时窗内两次连续调用
        // 日期不得回落，也不得因为任何 UTC 换算产生「昨天」名。
        var localMidnight = new DateTime(2026, 11, 1, 0, 0, 0); // UTC+8 00:00，UTC 仍为 10-31
        Assert.Equal("audit-2026-11-01", AuditLogFileNames.BaseName(localMidnight));

        var localSevenFiftyNine = new DateTime(2026, 11, 1, 7, 59, 0);
        Assert.Equal("audit-2026-11-01", AuditLogFileNames.BaseName(localSevenFiftyNine));
    }

    [Fact]
    public void BaseName_Should_Format_Single_Digit_Month_And_Day_With_Padding()
    {
        // 命名形状与既有文件族一致：月/日必须补零，否则与审计目录里既有文件
        // （以及 IPC/UI 的 glob 匹配）错位。
        var day = new DateTime(2026, 1, 5, 12, 0, 0);
        Assert.Equal("audit-2026-01-05", AuditLogFileNames.BaseName(day));
    }
}
