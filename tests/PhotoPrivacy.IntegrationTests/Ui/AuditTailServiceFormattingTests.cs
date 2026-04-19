using PhotoPrivacy.Ui;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class AuditTailServiceFormattingTests
{
    [Fact]
    public void ParseAuditLine_Should_Map_FileProcessingSucceeded_Event()
    {
        const string line = "{\"event_type\":\"file_processing_succeeded\",\"timestamp_utc\":\"2026-04-19T04:00:00.0000000+00:00\",\"source_path_masked\":\"D:/hot/***/a.jpg\",\"message\":\"ok\",\"data\":null}";

        var parsed = AuditTailService.ParseAuditLine(line, includeDetailedEvents: false);

        Assert.NotNull(parsed);
        Assert.Equal("✅ 清理完成", parsed!.DisplayEvent);
        Assert.Equal("D:/hot/***/a.jpg", parsed.SourcePathMasked);
        Assert.Equal("ok", parsed.Message);
    }

    [Fact]
    public void ParseAuditLine_Should_Filter_FileDetected_When_Detailed_Disabled()
    {
        const string line = "{\"event_type\":\"file_detected\",\"timestamp_utc\":\"2026-04-19T04:00:00.0000000+00:00\",\"source_path_masked\":\"D:/hot/***/a.jpg\",\"message\":\"detected\",\"data\":null}";

        var parsed = AuditTailService.ParseAuditLine(line, includeDetailedEvents: false);

        Assert.Null(parsed);
    }

    [Fact]
    public void ParseAuditLine_Should_Keep_FileDetected_When_Detailed_Enabled()
    {
        const string line = "{\"event_type\":\"file_detected\",\"timestamp_utc\":\"2026-04-19T04:00:00.0000000+00:00\",\"source_path_masked\":\"D:/hot/***/a.jpg\",\"message\":\"detected\",\"data\":null}";

        var parsed = AuditTailService.ParseAuditLine(line, includeDetailedEvents: true);

        Assert.NotNull(parsed);
        Assert.Equal("🔍 检测到文件", parsed!.DisplayEvent);
    }

    [Fact]
    public void BuildAuditPath_Should_Use_HotFolder_Audit_Subdirectory()
    {
        var day = new DateTime(2026, 4, 19);

        var path = AuditTailService.BuildAuditPath(@"D:\hot", day);

        Assert.Equal(@"D:\hot\_audit\audit-2026-04-19.jsonl", path);
    }
}
