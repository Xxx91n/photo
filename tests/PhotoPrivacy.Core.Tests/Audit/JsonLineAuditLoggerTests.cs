using PhotoPrivacy.Core.Audit;

namespace PhotoPrivacy.Core.Tests.Audit;

public sealed class JsonLineAuditLoggerTests
{
    private const long MaxFileSizeBytes = 100 * 1024 * 1024;

    private static string BaseFileName() => $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl";

    private static string ShardFileName(int seq) => $"audit-{DateTime.UtcNow:yyyy-MM-dd}_{seq}.jsonl";

    private static AuditEvent Event(string taskId = "t-1") => new(
        EventType: "file_processing_succeeded",
        Level: AuditLevel.Info,
        TimestampUtc: DateTimeOffset.UtcNow,
        TaskId: taskId,
        SourcePath: @"C:\Users\alice\Pictures\a.jpg",
        Message: "ok",
        Data: null);

    private static void FillToLimit(string path)
    {
        using var fs = File.Create(path);
        fs.SetLength(MaxFileSizeBytes + 1);
    }

    [Fact]
    public async Task WriteAsync_Should_Write_One_Json_Line_With_Utc_Timestamp_And_Masked_Path()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false);
            var ev = new AuditEvent(
                EventType: "file_processing_succeeded",
                Level: AuditLevel.Info,
                TimestampUtc: DateTimeOffset.UtcNow,
                TaskId: "t-1",
                SourcePath: @"C:\Users\alice\Pictures\a.jpg",
                Message: "ok",
                Data: null);

            await logger.WriteAsync(ev, CancellationToken.None);
            logger.Dispose();

            var file = Directory.GetFiles(dir, "audit-*.jsonl").Single();
            var line = File.ReadLines(file).Single();
            Assert.Contains("file_processing_succeeded", line);
            Assert.Contains("[redacted]", line);
            Assert.DoesNotContain("alice", line, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rollover_Should_Append_To_Active_Shard_Until_Full_Then_Advance_Seq()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // 基础名已满 → 第一批落到 _1
            FillToLimit(Path.Combine(dir, BaseFileName()));
            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                await logger.WriteAsync(Event(), CancellationToken.None);
            }
            Assert.True(File.Exists(Path.Combine(dir, ShardFileName(1))));
            Assert.Single(File.ReadLines(Path.Combine(dir, ShardFileName(1))));

            // _1 未满 → 后续批次续写 _1，不新建 _2
            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                await logger.WriteAsync(Event(), CancellationToken.None);
            }
            Assert.Equal(2, File.ReadLines(Path.Combine(dir, ShardFileName(1))).Count());
            Assert.False(File.Exists(Path.Combine(dir, ShardFileName(2))));

            // _1 已满 → 下一批新建 _2
            FillToLimit(Path.Combine(dir, ShardFileName(1)));
            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                await logger.WriteAsync(Event(), CancellationToken.None);
            }
            Assert.True(File.Exists(Path.Combine(dir, ShardFileName(2))));
            Assert.Single(File.ReadLines(Path.Combine(dir, ShardFileName(2))));

            // _2 未满 → 续写 _2，不新建 _3
            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                await logger.WriteAsync(Event(), CancellationToken.None);
            }
            Assert.Equal(2, File.ReadLines(Path.Combine(dir, ShardFileName(2))).Count());
            Assert.False(File.Exists(Path.Combine(dir, ShardFileName(3))));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rollover_Should_Not_Evict_Historical_Files_When_Sharding()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // 29 个历史日期文件，CreationTimeUtc 固定为近期早于当天，保证保留策略按日期稳定排序
            for (var i = 3; i <= 31; i++)
            {
                var hist = Path.Combine(dir, $"audit-2026-10-{i:D2}.jsonl");
                File.WriteAllText(hist, "{}" + Environment.NewLine);
                File.SetCreationTimeUtc(hist, new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc));
            }

            // 当天基础名已满 → 触发分卷
            FillToLimit(Path.Combine(dir, BaseFileName()));

            // 两个实例各写一批：修复后两者都续写 _1，不新建 _2，文件总数 31 不超上限
            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                await logger.WriteAsync(Event(), CancellationToken.None);
            }
            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                await logger.WriteAsync(Event(), CancellationToken.None);
                logger.CleanupExpired();
            }

            // 历史文件全部保留，未被 31 上限淘汰
            for (var i = 3; i <= 31; i++)
            {
                Assert.True(File.Exists(Path.Combine(dir, $"audit-2026-10-{i:D2}.jsonl")));
            }
            // 分卷复用 _1（两行），没有 _2；总数 31 恰好不超上限
            Assert.Equal(2, File.ReadLines(Path.Combine(dir, ShardFileName(1))).Count());
            Assert.False(File.Exists(Path.Combine(dir, ShardFileName(2))));
            Assert.Equal(31, Directory.GetFiles(dir, "audit-*.jsonl").Length);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

