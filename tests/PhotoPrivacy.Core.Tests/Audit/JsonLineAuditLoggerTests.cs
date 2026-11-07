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

    private static string CreateAuditFile(string dir, DateTime creationTimeUtc)
    {
        // 文件名按创建日期派生即可读；判龄与计数只认 CreationTimeUtc（#41/#208），名字不影响行为。
        var path = Path.Combine(dir, $"audit-{creationTimeUtc:yyyy-MM-dd}.jsonl");
        File.WriteAllText(path, "{}" + Environment.NewLine);
        File.SetCreationTimeUtc(path, creationTimeUtc);
        return path;
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
    public async Task WriteAsync_Should_Write_Into_Local_Day_Filename_Not_Utc()
    {
        // #129 决策钉死：写入端文件名按本地「今天」分卷（与 IPC 回填、UI tail 同口径），
        // UTC 只保留在行内 timestamp_utc。本用例断言文件名 = 本地日期名。
        // CI（ubuntu，本地时区=UTC）上本地=UTC，无法靠红灯区分基准——UTC+8 00:00–07:59
        // 分叉时窗的行为由 AuditLogFileNamesTests 的窗口用例钉死，本用例钉「入口进文件名」。
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false);
            var ev = new AuditEvent(
                EventType: "file_processing_succeeded",
                Level: AuditLevel.Info,
                TimestampUtc: DateTimeOffset.UtcNow,
                TaskId: "t-129",
                SourcePath: @"C:\Users\alice\Pictures\a.jpg",
                Message: "ok",
                Data: null);

            await logger.WriteAsync(ev, CancellationToken.None);
            logger.Dispose();

            var expected = $"audit-{DateTime.Now:yyyy-MM-dd}.jsonl";
            var file = Directory.GetFiles(dir, "audit-*.jsonl").Single();
            Assert.Equal(expected, Path.GetFileName(file));

            // 行内时间戳仍是 UTC 表示（#102 水位比较的前提），与文件名日期正交。
            var line = File.ReadLines(file).Single();
            Assert.Contains("\"timestamp_utc\"", line);
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
    public void CleanupExpired_Should_Delete_Files_Older_Than_RetainDays_And_Keep_Recent()
    {
        // #41 清理双闸之一：按创建时间判龄删除（#208 判据不变）。超龄删，保留窗口内不删。
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var anchor = DateTime.UtcNow.Date;
            var expired = CreateAuditFile(dir, anchor.AddDays(-31));
            var recent = CreateAuditFile(dir, anchor.AddDays(-1));

            using (var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false))
            {
                logger.CleanupExpired();
            }

            Assert.False(File.Exists(expired));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanupExpired_Should_Evict_Oldest_First_When_Count_Exceeds_Limit()
    {
        // #41 清理双闸之二：31 文件计数上限（分卷计入 audit-*.jsonl glob）是磁盘保护兜底，
        // 到上限最旧创建时间先删。判龄窗口放大到 365 天，让本用例只测计数闸。
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var anchor = DateTime.UtcNow.Date;
            var paths = new List<string>();
            for (var offset = 0; offset < 33; offset++)
            {
                paths.Add(CreateAuditFile(dir, anchor.AddDays(-offset)));
            }

            using (var logger = new JsonLineAuditLogger(dir, retainDays: 365, diagnosticMode: false))
            {
                logger.CleanupExpired();
            }

            Assert.Equal(31, Directory.GetFiles(dir, "audit-*.jsonl").Length);
            // 淘汰的必须是最旧两个（offset 32、31），其余全部保留
            Assert.False(File.Exists(paths[32]));
            Assert.False(File.Exists(paths[31]));
            for (var offset = 0; offset <= 30; offset++)
            {
                Assert.True(File.Exists(paths[offset]), $"offset {offset} should be retained");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CleanupExpired_Should_Skip_Age_Cleanup_When_RetainDays_Is_Not_Positive(int retainDays)
    {
        // #41 防退化配置清空审计面：retain_days<=0 视为「不做年龄清理」（fail-safe，
        // 对齐 BackupRetentionService.DeleteExpiredTtl 的 <=0 兜底），旧审计文件不因配置退化被全删。
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var anchor = DateTime.UtcNow.Date;
            var files = new[]
            {
                CreateAuditFile(dir, anchor.AddDays(-100)),
                CreateAuditFile(dir, anchor.AddDays(-60)),
                CreateAuditFile(dir, anchor.AddDays(-1))
            };

            using (var logger = new JsonLineAuditLogger(dir, retainDays, diagnosticMode: false))
            {
                logger.CleanupExpired();
            }

            foreach (var file in files)
            {
                Assert.True(File.Exists(file), $"{Path.GetFileName(file)} should be retained");
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanupExpired_Should_Still_Enforce_Count_Limit_When_RetainDays_Is_Not_Positive()
    {
        // #41 清理双闸各自独立：retain_days<=0 只关年龄闸，31 文件计数上限（磁盘保护兜底）仍生效。
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var anchor = DateTime.UtcNow.Date;
            var paths = new List<string>();
            for (var offset = 0; offset < 33; offset++)
            {
                paths.Add(CreateAuditFile(dir, anchor.AddDays(-offset)));
            }

            using (var logger = new JsonLineAuditLogger(dir, retainDays: 0, diagnosticMode: false))
            {
                logger.CleanupExpired();
            }

            Assert.Equal(31, Directory.GetFiles(dir, "audit-*.jsonl").Length);
            Assert.False(File.Exists(paths[32]));
            Assert.False(File.Exists(paths[31]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CleanupExpired_Should_Skip_Locked_File_And_Leave_Trace_On_Windows()
    {
        // #41 钉删除失败分支：单文件删除失败（Windows sharing violation；POSIX unlink 对
        // 打开文件成功，故 Windows 专属早退）不炸穿清理链——其余超龄文件照删、失败文件保留，
        // 且以 audit_cleanup_delete_failed 审计事件留痕（不变量④）。
        if (!OperatingSystem.IsWindows()) return;

        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var anchor = DateTime.UtcNow.Date;
            var locked = CreateAuditFile(dir, anchor.AddDays(-40));
            var other = CreateAuditFile(dir, anchor.AddDays(-35));

            using (var lockStream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var logger = new JsonLineAuditLogger(dir, retainDays: 30, diagnosticMode: false);
                try
                {
                    logger.CleanupExpired();
                }
                finally
                {
                    logger.Dispose();
                }
            }

            Assert.True(File.Exists(locked), "locked file should survive this cycle");
            Assert.False(File.Exists(other), "other expired file should still be deleted");

            // 留痕事件经 Dispose 排干通道落进活动文件（命名走 #129 唯一入口，不手拼）
            var active = Path.Combine(dir, AuditLogFileNames.BaseName(DateTime.Now) + ".jsonl");
            Assert.Contains("audit_cleanup_delete_failed", File.ReadAllText(active));
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
            // 29 个历史日期文件：从捕获的 UTC 日期向前派生，全部落在 CleanupExpired 30 天保留窗口内
            var anchor = DateTime.UtcNow.Date;
            for (var offset = 1; offset <= 29; offset++)
            {
                var date = anchor.AddDays(-offset);
                var hist = Path.Combine(dir, $"audit-{date:yyyy-MM-dd}.jsonl");
                File.WriteAllText(hist, "{}" + Environment.NewLine);
                File.SetCreationTimeUtc(hist, date);
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
            for (var offset = 1; offset <= 29; offset++)
            {
                var date = anchor.AddDays(-offset);
                Assert.True(File.Exists(Path.Combine(dir, $"audit-{date:yyyy-MM-dd}.jsonl")));
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

