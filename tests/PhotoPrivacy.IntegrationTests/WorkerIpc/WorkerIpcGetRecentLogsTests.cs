using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Ipc;
using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.WorkerIpc;

/// <summary>
/// 行为测试：Worker IPC <c>GetRecentLogs</c> 读取端的外部契约（此前 tests/ 零覆盖，#138 补回归锚点）。
///
/// 读取端现行契约（<c>WorkerIpcRequestHandler.ReadRecentAuditLogs</c>，经本套用例钉死）：
/// ① 只回 Worker 本地「今天」单个基础名文件 <c>audit-&lt;yyyy-MM-dd&gt;.jsonl</c> 的末 ≤50 行，
///    按文件内行序原样返回（UI 侧再按时间戳排序，读取端不得自行重排）；
/// ② 历史日期文件与分卷文件 <c>audit-&lt;date&gt;_N.jsonl</c> 的行不返回——#135 记录的既有口径，
///    本套用例固化它，不是把它改成缺陷；
/// ③ 目录缺失、文件缺失、读取失败一律空数组且 ok:true，不向调用方抛出。
///
/// 日期基准说明：写入端、IPC 回填、UI tail 三处的审计文件名日期基准已随 #129 统一为本地
/// 「今天」（统一入口 <c>AuditLogFileNames.BaseName</c>，UTC 只保留在行内 <c>timestamp_utc</c>）。
/// 本套用例按本地日期取样，是统一基准后的契约锚点。注意 CI（ubuntu，本地时区=UTC）上
/// 本地=UTC，任一端单方面改回 UTC 基准在 CI 可能不触发红灯，时区≠UTC 的实机才会显红——
/// 三处口径的守卫靠 AuditLogFileNamesTests 与写入端命名用例，不靠 CI 时区巧合。
/// </summary>
public sealed class WorkerIpcGetRecentLogsTests
{
    private const int MaxLines = 50;

    [Fact]
    public async Task Handle_Should_Return_Last_50_Lines_Of_Todays_File_In_File_Order()
    {
        var logDir = NewLogDir();
        try
        {
            // 60 行 > 50：单日超限是常态（一批照片即可产生两位数条目）。
            var lines = Enumerable.Range(1, 60).Select(i => $"line-{i:D2}").ToArray();
            await File.WriteAllLinesAsync(Path.Combine(logDir, TodayFileName()), lines);

            var response = await GetRecentLogsAsync(logDir);

            // 防回潮形态：读取端从「末 ≤50 行」退化成全量返回（#102 原型实测的日志页回灌风暴
            // 会随单日行数增长而放大），或为省事按倒序/字典序截断（UI 侧按时间戳排序的输入
            // 前提被破坏，末尾行出现在页面顶部）。本断言钉死：条数 = 50、且等于文件内最后
            // 50 行的原有顺序。
            // 关联 issue #138（本票固化），#129/#135 的读取端回归锚点。
            Assert.Equal(lines[^MaxLines..], OkLines(response));
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task Handle_Should_Return_All_Lines_In_File_Order_When_Under_Limit()
    {
        var logDir = NewLogDir();
        try
        {
            var lines = new[] { "line-01", "line-02", "line-03" };
            await File.WriteAllLinesAsync(Path.Combine(logDir, TodayFileName()), lines);

            var response = await GetRecentLogsAsync(logDir);

            // 防回潮形态：未超限时读取端做去重/重排/补齐（例如按解析时间戳重排）——行序是
            // UI 排序的输入，被动过手脚后「文件内行序返回」的契约静默失效。
            // 关联 issue #138（本票固化），#129/#135 的读取端回归锚点。
            Assert.Equal(lines, OkLines(response));
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task Handle_Should_Not_Return_Lines_From_Older_Date_Files()
    {
        var logDir = NewLogDir();
        try
        {
            // 今天、昨天、前天三个文件同时在目录：只有今天基础名文件的行允许返回。
            var today = new[] { "today-01", "today-02" };
            await File.WriteAllLinesAsync(Path.Combine(logDir, TodayFileName()), today);
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, DateFileName(-1)), new[] { "yesterday-01" });
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, DateFileName(-2)), new[] { "two-days-ago-01" });

            var response = await GetRecentLogsAsync(logDir);

            // 防回潮形态：读取端把「单文件 tail」扩成「枚举 audit-*.jsonl 聚合」——历史日期
            // 行混进回灌，UI 日志页出现陈旧条目；同时钉死返回范围只含今天文件。日期基准
            // 已随 #129 统一为本地「今天」（写入端同口径），任一端单方面改回 UTC 都会让
            // 本锚点失配——时区≠UTC 的实机上显红，CI（UTC）上可能保持绿（见文件头说明）。
            // 关联 issue #129、#135。
            Assert.Equal(today, OkLines(response));
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_When_Only_Older_Date_Files_Exist()
    {
        var logDir = NewLogDir();
        try
        {
            // 只放昨天与更早的文件：今天文件缺失，历史行不得补位返回。
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, DateFileName(-1)), new[] { "yesterday-01" });
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, DateFileName(-2)), new[] { "two-days-ago-01" });

            var response = await GetRecentLogsAsync(logDir);

            // 防回潮形态：今天文件缺失时回退读最近的历史文件——这正是 #129 A 组实机形态
            // （写入端落昨天名、读取端找今天名）被「顺手兼容」后的静默错配：看起来恢复了
            // 回灌，实际读的是错位一天的数据，三处日期基准的分叉被掩盖而不是被修复。
            // 关联 issue #129。
            Assert.Empty(OkLines(response));
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task Handle_Should_Not_Return_Lines_From_Sharded_Files()
    {
        var logDir = NewLogDir();
        try
        {
            // 分卷形状与写入端一致：audit-<日期>_N.jsonl、seq 从 1 起（JsonLineAuditLogger 分卷实现；
            // 写入端文件名日期基准已随 #129 统一为本地「今天」，本用例只钉「_N 后缀不回」的排除规则）。
            // 今天基础名文件同时在：证明排除的是分卷后缀，不是整个目录。
            var today = new[] { "today-base-01" };
            await File.WriteAllLinesAsync(Path.Combine(logDir, TodayFileName()), today);
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, ShardedFileName(1)), new[] { "shard-1-01" });
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, ShardedFileName(2)), new[] { "shard-2-01" });

            var response = await GetRecentLogsAsync(logDir);

            // 防回潮形态：读取端把文件匹配从基础名扩到 _N 分卷（或枚举 audit-*.jsonl）——
            // 分卷行与基础名行重复/乱序回灌 UI。分卷行不经 GetRecentLogs 可见是 #135 记录的
            // 既有口径，是否改由 #135 裁决；本锚点让任何「顺手修」显式变红。
            // 关联 issue #135。
            Assert.Equal(today, OkLines(response));
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_When_Log_Directory_Missing()
    {
        // 只拼路径不创建目录：目录缺失是未启用监控/待机形态的常规外部状态。
        var missingDir = Path.Combine(
            Path.GetTempPath(), "photo-recent-logs-missing-" + Guid.NewGuid().ToString("N"));

        var response = await GetRecentLogsAsync(missingDir);

        // 防回潮形态：目录缺失分支被改成 DirectoryNotFoundException 上抛——一次目录缺失
        // 就把 IPC 循环带崩或让 UI 日志轮询报错；#124 落地的待机零目录副作用形态也落在
        // 这条路径上。
        // 关联 issue #138（本票固化），#129/#135 的读取端回归锚点。
        Assert.Empty(OkLines(response));
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_When_Todays_File_Missing()
    {
        var logDir = NewLogDir();
        try
        {
            // 目录在、今天文件不在：Worker 刚换审计目录或当日尚无写入时的常规形态。
            var response = await GetRecentLogsAsync(logDir);

            // 防回潮形态：文件缺失分支被 FileNotFoundException 上抛或改读目录里"最新的
            // 其他文件"兜底——前者炸 IPC，后者与「只回今天文件」的契约冲突。
            // 关联 issue #138（本票固化），#129/#135 的读取端回归锚点。
            Assert.Empty(OkLines(response));

            // 正向对照（R2「失效即红」自检）：同一目录补上今天文件后同一路径必须回行——
            // 把「空」钉在「文件缺失」这个原因上，排除「读取路径恒空」的假绿空间。
            await File.WriteAllLinesAsync(
                Path.Combine(logDir, TodayFileName()), new[] { "late-arrival" });
            Assert.Equal(new[] { "late-arrival" }, OkLines(await GetRecentLogsAsync(logDir)));
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_When_Audit_Log_Directory_Is_Not_Set()
    {
        // AuditLogDirectory 为 null：运行时未提供审计目录（待机/回落前形态）时的守卫分支。
        var response = await GetRecentLogsAsync(logDirectory: null);

        // 防回潮形态：null/空目录守卫被去掉——Path.Combine(null, ...) 抛 ArgumentNullException
        // 或把 "" 拼成相对路径在进程工作目录下乱找文件。
        // 关联 issue #138（本票固化），#129/#135 的读取端回归锚点。
        Assert.Empty(OkLines(response));
    }

    [Fact]
    public async Task Handle_Should_Return_Empty_When_Todays_File_Is_Locked()
    {
        var logDir = NewLogDir();
        var todayPath = Path.Combine(logDir, TodayFileName());
        await File.WriteAllTextAsync(todayPath, "locked-line" + Environment.NewLine);
        try
        {
            // FileShare.None 独占持有：Windows 必共享冲突；Linux 上 .NET 以 flock 模拟共享
            // 语义，读侧非阻塞 LOCK_SH 撞独占 LOCK_EX 同样抛 IOException。
            using (new FileStream(todayPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var response = await GetRecentLogsAsync(logDir);

                // 防回潮形态：catch-all 被移除或改为上抛——杀软扫描、备份工具持句柄这类
                // 一次瞬时读取失败就让 IPC 循环崩掉/客户端收到异常；同族约束见 #85
                // （日志失败不得吃掉回包）。固化「读取失败降级为空数组」的既有行为。
                // 关联 issue #138（本票固化），#129/#135 的读取端回归锚点。
                Assert.Empty(OkLines(response));
            }
        }
        finally
        {
            SafeDeleteDirectory(logDir);
        }
    }

    /// <summary>
    /// 断言回包为 ok:true 且带行集，返回行集——把「正常回包」前提集中一处，
    /// 让每个用例自己的断言只盯它钉的行为。
    /// </summary>
    private static string[] OkLines(WorkerIpcResponse response)
    {
        Assert.True(response.Ok, "GetRecentLogs 正常路径必须 ok:true");
        Assert.NotNull(response.Logs);
        return response.Logs.Lines;
    }

    private static string NewLogDir()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "photo-recent-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string TodayFileName() => DateFileName(0);

    private static string DateFileName(int dayOffset)
        => $"audit-{DateTime.Today.AddDays(dayOffset):yyyy-MM-dd}.jsonl";

    private static string ShardedFileName(int seq)
        => $"audit-{DateTime.Today:yyyy-MM-dd}_{seq}.jsonl";

    /// <summary>
    /// 调用 <c>GetRecentLogs</c> 并返回回包：注入审计目录构造假件与处理器，
    /// 直调 <see cref="WorkerIpcRequestHandler.HandleAsync"/>（外部缝），并顺手断言
    /// 该方法不得触发停机。
    /// </summary>
    private static async Task<WorkerIpcResponse> GetRecentLogsAsync(string? logDirectory)
    {
        var runtime = new FakeWorkerIpcRuntime { AuditLogDirectory = logDirectory };
        var handler = new WorkerIpcRequestHandler(runtime, NullLogger<WorkerIpcRequestHandler>.Instance);
        var (response, shouldShutdown) = await handler.HandleAsync(
            new WorkerIpcRequest(WorkerIpcMethods.GetRecentLogs, "recent-logs"),
            CancellationToken.None);
        Assert.False(shouldShutdown, "GetRecentLogs 不得触发停机");
        return response;
    }

    /// <summary>
    /// 删除临时目录：Windows 下句柄释放有延迟，IOException / UnauthorizedAccessException
    /// 时限 3 s 重试；清理失败不得掩盖用例自身的断言结果，超时后静默放弃（残留临时目录
    /// 不影响判绿判红）。
    /// </summary>
    private static void SafeDeleteDirectory(string directoryPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (IOException)
            {
                // Windows 下句柄释放有延迟：到点重试。
            }
            catch (UnauthorizedAccessException)
            {
                // 只读位等残留：到点重试。
            }
        }
    }
}
