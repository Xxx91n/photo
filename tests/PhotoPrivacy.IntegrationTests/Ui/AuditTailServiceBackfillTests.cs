using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票号05 — 日志双通道收口回归：FSW tail（AuditTailService）+ IPC backfill 合并去重。
/// ADR 0046 语义保持：backfill 失败静默重试、不杀 UI；ADR 0037：清空后旧日志不得重现。
/// </summary>
public sealed class AuditTailServiceBackfillTests
{

    private static string Line(string id, string iso) =>
        $"{{\"event_type\":\"file_processing_succeeded\",\"level\":\"INFO\",\"timestamp_utc\":\"{iso}\",\"source_path_masked\":\"D:/hot/***/{id}.jpg\",\"message\":\"{id}\",\"data\":null}}";

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(50);
        }
        Assert.True(condition(), "condition not met within timeout");
    }

    private static string NewAuditDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-audit-bftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- 纯函数：MergeBackfillLines 合并/去重/排序 ----

    [Fact]
    public void MergeBackfillLines_Should_Dedup_Whitespace_And_Sort_Ascending()
    {
        var l1 = Line("a", "2026-08-31T04:00:01.0000000+00:00");
        var l2 = Line("b", "2026-08-31T04:00:02.0000000+00:00");

        var merged = AuditTailService.MergeBackfillLines(new[] { l2, "   ", l1, l2, l1 });

        Assert.Equal(new[] { l1, l2 }, merged);
    }

    [Fact]
    public void MergeBackfillLines_Should_Place_Unparseable_Timestamp_At_End_Stable()
    {
        var l1 = Line("a", "2026-08-31T04:00:01.0000000+00:00");
        var l2 = Line("b", "2026-08-31T04:00:02.0000000+00:00");
        var noTs = "{\"event_type\":\"file_processing_succeeded\",\"level\":\"INFO\",\"message\":\"no-ts\",\"data\":null}";

        var merged = AuditTailService.MergeBackfillLines(new[] { noTs, l2, l1 });

        Assert.Equal(new[] { l1, l2, noTs }, merged);
    }

    // ---- 集成：backfill 与 FSW 重叠不重复 ----

    [Fact]
    public async Task Backfill_Then_Fsw_Overlapping_Lines_Should_Not_Duplicate()
    {
        var auditDir = NewAuditDir();
        try
        {
            var l1 = Line("l1", "2026-08-31T04:00:01.0000000+00:00");
            var l2 = Line("l2", "2026-08-31T04:00:02.0000000+00:00");
            var l3 = Line("l3", "2026-08-31T04:00:03.0000000+00:00");
            var auditFile = Path.Combine(auditDir, $"audit-{DateTime.Today:yyyy-MM-dd}.jsonl");
            await File.WriteAllTextAsync(auditFile, l1 + Environment.NewLine);

            var batches = new List<IReadOnlyList<AuditLogEntry>>();
            var service = new AuditTailService(
                logDirectory: auditDir,
                onBatch: batches.Add,
                getLogLevel: () => "info",
                backfillFetcher: _ => Task.FromResult<string[]?>(new[] { l1, l2 }),
                backfillRetryInterval: TimeSpan.FromMilliseconds(50));
            service.Start();

            // Worker（同时是 FSW 监听的写入方）在 Start 之后补写 l2/l3：
            // l2 与 backfill 已交付的原始行完全相同，必须被去重。
            await File.AppendAllTextAsync(auditFile, l2 + Environment.NewLine + l3 + Environment.NewLine);
            await Task.Delay(2500); // LoopAsync 1s 轮询 + FSW 事件窗口
            await service.StopAsync();

            var messages = batches.SelectMany(b => b).Select(e => e.Message).OrderBy(m => m, StringComparer.Ordinal).ToList();
            Assert.Equal(new[] { "l1", "l2", "l3" }, messages);
        }
        finally
        {
            TryDeleteDir(auditDir);
        }
    }

    [Fact]
    public async Task Backfill_Failure_Should_Retry_Silently_Then_Succeed()
    {
        var auditDir = NewAuditDir();
        try
        {
            var l1 = Line("r1", "2026-08-31T04:00:01.0000000+00:00");
            var calls = 0;
            var batches = new List<IReadOnlyList<AuditLogEntry>>();
            var service = new AuditTailService(
                logDirectory: auditDir,
                onBatch: batches.Add,
                getLogLevel: () => "info",
                backfillFetcher: _ =>
                {
                    calls++;
                    return calls >= 3
                        ? Task.FromResult<string[]?>(new[] { l1 })
                        : Task.FromResult<string[]?>(null);
                },
                backfillRetryInterval: TimeSpan.FromMilliseconds(50));
            service.Start();

            await WaitForAsync(() => calls >= 3, TimeSpan.FromSeconds(10));
            await service.StopAsync();

            Assert.Contains(batches.SelectMany(b => b).Select(e => e.Message), m => m == "r1");
            Assert.Equal(1, batches.SelectMany(b => b).Count(e => e.Message == "r1"));
        }
        finally
        {
            TryDeleteDir(auditDir);
        }
    }

    [Fact]
    public async Task Backfill_Fetcher_Throwing_Should_Be_Treated_As_Unreachable()
    {
        var auditDir = NewAuditDir();
        try
        {
            var l1 = Line("t1", "2026-08-31T04:00:01.0000000+00:00");
            var calls = 0;
            var batches = new List<IReadOnlyList<AuditLogEntry>>();
            var service = new AuditTailService(
                logDirectory: auditDir,
                onBatch: batches.Add,
                getLogLevel: () => "info",
                backfillFetcher: _ =>
                {
                    calls++;
                    return calls >= 2
                        ? Task.FromResult<string[]?>(new[] { l1 })
                        : throw new IOException("pipe broken");
                },
                backfillRetryInterval: TimeSpan.FromMilliseconds(50));
            service.Start();

            await WaitForAsync(() => calls >= 2, TimeSpan.FromSeconds(10));
            await service.StopAsync();

            Assert.Contains(batches.SelectMany(b => b), e => e.Message == "t1");
        }
        finally
        {
            TryDeleteDir(auditDir);
        }
    }

    // ---- ADR 0037：清空后旧日志不得重现，去重记忆保留 ----

    [Fact]
    public async Task NotifyLogsCleared_Should_Drain_Pending_And_Keep_Dedup_Memory()
    {
        var auditDir = NewAuditDir();
        try
        {
            var l1 = Line("c1", "2026-08-31T04:00:01.0000000+00:00");
            var l2 = Line("c2", "2026-08-31T04:00:02.0000000+00:00");
            var l3 = Line("c3", "2026-08-31T04:00:03.0000000+00:00");
            var batches = new List<IReadOnlyList<AuditLogEntry>>();
            var service = new AuditTailService(
                logDirectory: auditDir,
                onBatch: batches.Add,
                getLogLevel: () => "info",
                backfillFetcher: _ => Task.FromResult<string[]?>(new[] { l1, l2 }),
                backfillRetryInterval: TimeSpan.FromMilliseconds(50));
            service.Start();
            await Task.Delay(300); // backfill 交付进 pending

            service.NotifyLogsCleared(); // 清空：drain pending + 水位到尾，_deliveredLines 保留

            // 清空后 Worker 重写 c2（与已清空的历史行原文相同）+ 新事件 c3：
            // c2 是已交付过的历史行，凭保留的去重记忆不得重现。
            var auditFile = Path.Combine(auditDir, $"audit-{DateTime.Today:yyyy-MM-dd}.jsonl");
            await File.AppendAllTextAsync(auditFile, l2 + Environment.NewLine + l3 + Environment.NewLine);
            await Task.Delay(2500);
            await service.StopAsync();

            var messages = batches.SelectMany(b => b).Select(e => e.Message).ToList();
            Assert.DoesNotContain("c1", messages);
            Assert.DoesNotContain("c2", messages);
            Assert.Contains("c3", messages);
        }
        finally
        {
            TryDeleteDir(auditDir);
        }
    }

    [Fact]
    public async Task Start_Without_Fetcher_Should_Still_Tail_File_Only()
    {
        var auditDir = NewAuditDir();
        try
        {
            var l1 = Line("n1", "2026-08-31T04:00:01.0000000+00:00");
            var l2 = Line("n2", "2026-08-31T04:00:02.0000000+00:00");
            var auditFile = Path.Combine(auditDir, $"audit-{DateTime.Today:yyyy-MM-dd}.jsonl");
            await File.WriteAllTextAsync(auditFile, l1 + Environment.NewLine);
            var batches = new List<IReadOnlyList<AuditLogEntry>>();
            var service = new AuditTailService(
                logDirectory: auditDir,
                onBatch: batches.Add,
                getLogLevel: () => "info");
            service.Start(); // 无 fetcher：启动水位跳到文件尾，启动前已存在的 l1 是历史不回放
            await File.AppendAllTextAsync(auditFile, l2 + Environment.NewLine);
            await Task.Delay(2500);
            await service.StopAsync();

            Assert.DoesNotContain(batches.SelectMany(b => b), e => e.Message == "n1");
            Assert.Contains(batches.SelectMany(b => b), e => e.Message == "n2");
        }
        finally
        {
            TryDeleteDir(auditDir);
        }
    }

    // ---- 源断言 guard（票号04 同模式）：入口单一性 ----

    [Fact]
    public void MainWindow_Source_Should_Not_Reference_BackfillRecentLogs()
    {
        var source = File.ReadAllText(
            Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs"));
        Assert.DoesNotContain("BackfillRecentLogs", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditTailService_Source_Should_Own_Backfill_Fetcher_And_Merge()
    {
        var source = File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "src", "PhotoPrivacy.Ui", "Services", "AuditTailService.cs"));
        Assert.Contains("backfillFetcher", source, StringComparison.Ordinal);
        Assert.Contains("MergeBackfillLines", source, StringComparison.Ordinal);
        Assert.Contains("NotifyLogsCleared", source, StringComparison.Ordinal);
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort temp cleanup; OS temp will be reclaimed
        }
    }
}
