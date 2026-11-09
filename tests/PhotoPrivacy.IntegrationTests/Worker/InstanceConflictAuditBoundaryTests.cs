using System.Diagnostics;
using System.Text.Json;
using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.Worker;

/// <summary>
/// 行为测试：#127 冲突/让位留痕的「目录创建边界」。
/// 待机与配置解析路径零副作用（#124 / #153 已逐项确认做完），只有真正写审计的冲突/让位退出路径允许建目录；
/// 只读 / 不可写安装目录下该创建必须有界降级，且降级本身可读、不静默。
/// </summary>
public sealed class InstanceConflictAuditBoundaryTests
{
    [Fact]
    public async Task Conflict_Audit_Should_Degrade_Readably_When_Audit_Directory_Cannot_Be_Created()
    {
        // 要防的老毛病：JsonLineAuditLogger 构造里的 Directory.CreateDirectory 在只读安装目录抛出，
        // 被外层空 catch 静默吞掉——冲突事实只在 stderr 留一句，留痕为何消失无据可查（#127）。
        var root = NewTempRoot();
        var installRoot = Path.Combine(root, "install");
        Directory.CreateDirectory(installRoot);

        // 「父分量是文件」在 Windows 与 POSIX 上都让 Directory.CreateDirectory 抛 IOException，
        // 与 Program Files / 受限账户属同一失败类，但不依赖 ACL、特权或 root 权限假设。
        var blocker = Path.Combine(installRoot, "hot");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var audit = Path.Combine(blocker, "_audit");
        var args = BuildArgs(root, audit);

        try
        {
            var outcome = await InstanceConflictAudit.TryWriteAsync(
                args, RuntimeMode.Background, "Global\\pp-127-test-mutex");

            var record = outcome.DegradationRecord;
            Assert.NotNull(record);
            Assert.Contains("conflict audit degraded", record!, StringComparison.Ordinal);
            Assert.Contains(audit, record!, StringComparison.Ordinal);
            Assert.Contains("检测到重复启动并已拒绝", record!, StringComparison.Ordinal);
            // 降级阶段必须可辨：只断言「有降级记录」会把「配置解析失败」也算成通过，
            // 掩盖「目录不可建」这条本票要守的分支。
            Assert.Contains("审计目录不可写", record!, StringComparison.Ordinal);
            // 降级记录必须保留冲突上下文；缺 requested_mode 会让排障时分不清后台/服务形态。
            Assert.Contains("requested_mode=background", record!, StringComparison.Ordinal);
            // 防：降级路径为了建出 audit 目录而改写父分量（#127）。
            // 本固定装置下 Directory.Exists(audit) 结构上恒假、没有鉴别力；
            // 改为盯住父分量仍是文件，配合上面的 stage/requested_mode 断言确认降级确实发生。
            Assert.True(File.Exists(blocker), "降级路径不得改写父分量");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Single_Writer_Yield_Should_Degrade_Readably_On_Unwritable_Audit_Directory()
    {
        // 要防的老毛病：#162 让位留痕与重复启动留痕共用写通道，只修一条入口时另一条仍静默失败。
        var root = NewTempRoot();
        var installRoot = Path.Combine(root, "install");
        Directory.CreateDirectory(installRoot);
        var blocker = Path.Combine(installRoot, "hot");
        await File.WriteAllTextAsync(blocker, "not a directory");
        var audit = Path.Combine(blocker, "_audit");
        var args = BuildArgs(root, audit);

        try
        {
            var outcome = await InstanceConflictAudit.TryWriteSingleWriterYieldAsync(
                args, RuntimeMode.Service, audit);

            var record = outcome.DegradationRecord;
            Assert.NotNull(record);
            Assert.Contains("single_writer_gate", record!, StringComparison.Ordinal);
            Assert.Contains("检测到另一 Worker", record!, StringComparison.Ordinal);
            Assert.Contains("审计目录不可写", record!, StringComparison.Ordinal);
            Assert.Contains("requested_mode=service", record!, StringComparison.Ordinal);
            // 防：让位路径为了建目录改写父分量（同 #127 的目录创建边界）。
            Assert.True(File.Exists(blocker), "降级路径不得改写父分量");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Single_Writer_Yield_Should_Write_Audit_Line_When_Directory_Is_Writable()
    {
        // 要防的老毛病（#211）：只把重复启动入口改成同步写，让位入口仍走后台队列、失败仍静默丢行。
        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");

        try
        {
            var outcome = await InstanceConflictAudit.TryWriteSingleWriterYieldAsync(
                BuildArgs(root, audit), RuntimeMode.Service, audit);

            Assert.Null(outcome.DegradationRecord);
            var content = await File.ReadAllTextAsync(ConflictFile(audit));
            Assert.Contains("\"event_type\":\"instance_conflict\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("single_writer_gate", content, StringComparison.Ordinal);
            Assert.Contains("检测到另一 Worker", content, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Write_When_Audit_Directory_Is_Writable()
    {
        // 要防的老毛病：把「配置解析期不建目录」误做成「所有路径都不建目录」，
        // 重复启动退出时冲突留痕彻底消失（非待机写路径仍须建目录并落盘）。
        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");
        var args = BuildArgs(root, audit);

        try
        {
            var outcome = await InstanceConflictAudit.TryWriteAsync(
                args, RuntimeMode.Background, "Global\\pp-127-test-mutex");

            Assert.Null(outcome.DegradationRecord);
            Assert.True(Directory.Exists(audit), "非待机写路径必须建出审计目录");

            // 同步写路径返回即已落盘：这里不轮询等待，直接读当天文件（#211 裁决）。
            var file = ConflictFile(audit);
            Assert.True(File.Exists(file), "冲突行必须在返回前落到当天审计文件");
            var content = await File.ReadAllTextAsync(file);
            Assert.Contains("\"event_type\":\"instance_conflict\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("检测到重复启动并已拒绝", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Write_After_Transient_Sharing_Conflict_Clears()
    {
        // 要防的老毛病（#211）：3 次重试原来挂在只入队、不碰文件的 WriteAsync 外面，
        // 常驻 Worker 短暂占着 audit-*.jsonl 时重试根本不触发。改成同步写后，
        // 300 ms 的间隔要能覆盖「对面开文件就一小会儿」的真实碰撞，而不是直接降级丢行。
        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");
        Directory.CreateDirectory(audit);
        var target = ConflictFile(audit);
        await File.WriteAllTextAsync(target, string.Empty);

        FileStream? hold = null;
        try
        {
            hold = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (!SharingConflictIsEnforced(target))
            {
                // POSIX 侧 FileShare 是协作式建议锁，同进程第二个句柄不一定被拦；
                // 共享冲突本身由 Windows CI 腿与实机覆盖，这里不硬造假失败。
                return;
            }

            var writeTask = InstanceConflictAudit.TryWriteAsync(
                BuildArgs(root, audit), RuntimeMode.Background, "Global\\pp-211-shared-file");

            // 第一次尝试撞锁、第二次（~300 ms）仍撞锁、第三次（~600 ms）前放开，
            // 正好验证重试真的包在写文件动作外。
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            Assert.False(writeTask.IsCompleted, "文件被占着时第一次同步写不应成功");
            hold.Dispose();
            hold = null;

            var outcome = await writeTask;
            Assert.Null(outcome.DegradationRecord);
            var content = await File.ReadAllTextAsync(target);
            Assert.Contains("\"event_type\":\"instance_conflict\"", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            hold?.Dispose();
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Degrade_Readably_When_Audit_File_Sharing_Conflict_Persists()
    {
        // 要防的老毛病（#211）：真实落盘失败被后台任务吞掉，Dispose 的空 catch 也不留痕，
        // 排障时分不清「没写」和「写了但丢」。同步写路径必须把共享冲突当场降级成可读记录。
        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");
        Directory.CreateDirectory(audit);
        var target = ConflictFile(audit);
        await File.WriteAllTextAsync(target, string.Empty);

        FileStream? hold = null;
        try
        {
            hold = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (!SharingConflictIsEnforced(target))
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var outcome = await InstanceConflictAudit.TryWriteAsync(
                BuildArgs(root, audit), RuntimeMode.Background, "Global\\pp-211-shared-file");
            stopwatch.Stop();

            // 3 次尝试之间固定各等 300 ms：真重试的失败耗时不会低于 ~600 ms。
            // 重试被删掉或改成空转时，这行断言当场失败。
            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromMilliseconds(500),
                $"持续撞锁时应完成 3 次有间隔的尝试，实际只等了 {stopwatch.ElapsedMilliseconds} ms");

            var record = outcome.DegradationRecord;
            Assert.NotNull(record);
            Assert.Contains("conflict audit degraded", record!, StringComparison.Ordinal);
            Assert.Contains("冲突留痕写入失败", record!, StringComparison.Ordinal);
            Assert.Contains(nameof(IOException), record!, StringComparison.Ordinal);
            Assert.Contains(audit, record!, StringComparison.Ordinal);
            Assert.Contains("检测到重复启动并已拒绝", record!, StringComparison.Ordinal);
            Assert.Equal(0, new FileInfo(target).Length);
        }
        finally
        {
            hold?.Dispose();
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Degrade_Readably_When_Audit_Target_Cannot_Be_Opened()
    {
        // 要防的老毛病（#211）：同步 append 失败被外层空 catch 吞掉，只留下一句「没写成」。
        // 「当天文件名被目录占住」在 Windows / POSIX 都会让打开写句柄失败，是跨平台可复现的落盘失败。
        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");
        Directory.CreateDirectory(audit);
        Directory.CreateDirectory(ConflictFile(audit));

        try
        {
            var outcome = await InstanceConflictAudit.TryWriteAsync(
                BuildArgs(root, audit), RuntimeMode.Background, "Global\\pp-211-target-not-file");

            var record = outcome.DegradationRecord;
            Assert.NotNull(record);
            Assert.Contains("冲突留痕写入失败", record!, StringComparison.Ordinal);
            Assert.Contains(audit, record!, StringComparison.Ordinal);
            Assert.Contains("检测到重复启动并已拒绝", record!, StringComparison.Ordinal);
            Assert.Contains("requested_mode=background", record!, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_And_Json_Line_Logger_Should_Emit_Same_Json_Fields()
    {
        // 要防的老毛病（#211）：两条写路径各自拼 JSON，字段名或 diagnostic_mode 以后各改各的，
        // UI tail 与排障看到的字段对不上。字段名集合必须由同一个拼装入口锁住。
        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");
        var loggerDirectory = Path.Combine(root, "install", "hot", "_logger");
        Directory.CreateDirectory(loggerDirectory);

        try
        {
            var outcome = await InstanceConflictAudit.TryWriteAsync(
                BuildArgs(root, audit), RuntimeMode.Background, "Global\\pp-211-format");
            Assert.Null(outcome.DegradationRecord);

            using (var logger = new JsonLineAuditLogger(
                loggerDirectory,
                retainDays: 30,
                diagnosticMode: AppConfig.Default.Audit.DiagnosticMode))
            {
                await logger.WriteAsync(
                    new AuditEvent(
                        EventType: "instance_conflict",
                        Level: AuditLevel.Warn,
                        TimestampUtc: DateTimeOffset.UtcNow,
                        TaskId: "t-211",
                        SourcePath: @"C:\photos\a.jpg",
                        Message: "format check",
                        Data: null),
                    CancellationToken.None);
            }

            var conflictLine = (await File.ReadAllLinesAsync(ConflictFile(audit))).Single();
            var loggerFile = Directory.GetFiles(loggerDirectory, "audit-*.jsonl").Single();
            var loggerLine = (await File.ReadAllLinesAsync(loggerFile)).Single();

            // 顺序不是契约，按字段名集合比：两条路径的键集合不能多也不能少。
            Assert.Equal(
                JsonFields(loggerLine).OrderBy(name => name, StringComparer.Ordinal),
                JsonFields(conflictLine).OrderBy(name => name, StringComparer.Ordinal));
            Assert.Equal(JsonBool(loggerLine, "diagnostic_mode"), JsonBool(conflictLine, "diagnostic_mode"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Config_Resolution_Should_Stay_Pure_Read_With_Write_Path_Owning_Directory_Creation()
    {
        // 要防的老毛病：#127 新加的「写路径有界创建」被顺手挪回 LoadConfigFromArgs，
        // 待机首启重新长出 hot/_audit，把 #124 / #153 已逐项确认做完的零副作用带回来。
        var root = NewTempRoot();
        var hot = Path.Combine(root, "install", "hot");
        var audit = Path.Combine(hot, "_audit");
        var args = BuildArgs(root, audit);

        try
        {
            var config = InstanceConflictAudit.LoadConfigFromArgs(args);

            Assert.Equal(audit, config.Audit.LogDirectory);
            // 只断言显式传入的 audit 目录：Watch.HotFolder 来自 AppConfig.Default 的
            // <BaseDirectory>/hot，不是本用例的 root/install/hot，断言后者恒真、没有鉴别力。
            Assert.False(Directory.Exists(audit), "配置解析仍是纯读，不得建出审计目录");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Degrade_When_Existing_Audit_Directory_Is_Not_Writable()
    {
        // 要防的老毛病：删掉可写性探针后，「目录已存在但当前账户不可写」不再被识别——
        // Directory.CreateDirectory 原样返回，JsonLineAuditLogger 构造不抛，后台落盘静默丢行（#127）。
        // Windows 的目录 ReadOnly 属性不阻止建文件，ACL 改写与 CI 账户耦合，故该分支只在 POSIX 构造
        // （与 SingleWriterGateTests 的 symlink 用例同一规则）；Windows 侧由实机只读目录验收覆盖。
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = NewTempRoot();
        var audit = Path.Combine(root, "install", "hot", "_audit");
        Directory.CreateDirectory(audit);
        var originalMode = File.GetUnixFileMode(audit);
        try
        {
            File.SetUnixFileMode(
                audit,
                UnixFileMode.UserRead | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            // 环境自检：以 root 运行时 0555 仍可写，该形态不可复现，避免假红。
            if (CanCreateFile(audit))
            {
                return;
            }

            var outcome = await InstanceConflictAudit.TryWriteAsync(
                BuildArgs(root, audit), RuntimeMode.Background, "Global\\pp-127-test-mutex");

            var record = outcome.DegradationRecord;
            Assert.NotNull(record);
            Assert.Contains("conflict audit degraded", record!, StringComparison.Ordinal);
            Assert.Contains(audit, record!, StringComparison.Ordinal);
            Assert.Contains("审计目录不可写", record!, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(audit, "audit-*.jsonl"));
        }
        finally
        {
            // 恢复权限失败也不得跳过清理：否则 /tmp 残留一个 0555 目录且测试以未处理异常失败。
            try
            {
                File.SetUnixFileMode(audit, originalMode);
            }
            catch
            {
                // best effort：目录即将被删除，恢复失败不得阻断清理。
            }

            TryDelete(root);
        }
    }

    private static bool CanCreateFile(string directory)
    {
        var path = Path.Combine(directory, ".write-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (File.Create(path))
            {
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            // 删除失败只当清理问题，不得被误判成「目录不可写」（否则下游断言假红）。
            try
            {
                File.Delete(path);
            }
            catch
            {
                // best effort：删除失败不得被误判成「目录不可写」。
            }
        }
    }

    private static string[] BuildArgs(string root, string auditDirectory)
    {
        // 故意指向不存在的 config：LoadConfigFromArgs 回落 AppConfig.Default，
        // 再由 --audit-folder 覆盖审计目录，避免把测试锚在测试宿主的 BaseDirectory 上。
        return ["--config", Path.Combine(root, "missing-config.json"), "--audit-folder", auditDirectory];
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-127-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ConflictFile(string auditDirectory) =>
        Path.Combine(auditDirectory, AuditLogFileNames.BaseName(DateTime.Now) + ".jsonl");

    private static bool SharingConflictIsEnforced(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static List<string> JsonFields(string line)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
    }

    private static bool JsonBool(string line, string propertyName)
    {
        using var document = JsonDocument.Parse(line);
        return document.RootElement.GetProperty(propertyName).GetBoolean();
    }

    private static void TryDelete(string root)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
    }
}
