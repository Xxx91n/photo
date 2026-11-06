using PhotoPrivacy.Worker;

namespace PhotoPrivacy.IntegrationTests.Worker;

/// <summary>
/// 行为测试：#127 冲突/让位留痕的「目录创建边界」。
/// 待机与配置解析路径零副作用（#124 / #153 已核销），只有真正写审计的冲突/让位退出路径允许建目录；
/// 只读 / 不可写安装目录下该创建必须有界降级，且降级本身可读、不静默。
/// </summary>
public sealed class InstanceConflictAuditBoundaryTests
{
    [Fact]
    public async Task Conflict_Audit_Should_Degrade_Readably_When_Audit_Directory_Cannot_Be_Created()
    {
        // 防的回潮形态：JsonLineAuditLogger 构造里的 Directory.CreateDirectory 在只读安装目录抛出，
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
            Assert.False(Directory.Exists(audit), "降级路径不得建出审计目录");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Single_Writer_Yield_Should_Degrade_Readably_On_Unwritable_Audit_Directory()
    {
        // 防的回潮形态：#162 让位留痕与重复启动留痕共用写通道，只修一条入口时另一条仍静默失败。
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
            Assert.False(Directory.Exists(audit));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Write_When_Audit_Directory_Is_Writable()
    {
        // 防的回潮形态：把「配置解析期不建目录」误做成「所有路径都不建目录」，
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

            var file = WaitForAuditFile(audit, TimeSpan.FromSeconds(5));
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
    public void Config_Resolution_Should_Stay_Pure_Read_With_Write_Path_Owning_Directory_Creation()
    {
        // 防的回潮形态：#127 新加的「写路径有界创建」被顺手挪回 LoadConfigFromArgs，
        // 待机首启重新长出 hot/_audit，把 #124 / #153 已核销的零副作用带回来。
        var root = NewTempRoot();
        var hot = Path.Combine(root, "install", "hot");
        var audit = Path.Combine(hot, "_audit");
        var args = BuildArgs(root, audit);

        try
        {
            var config = InstanceConflictAudit.LoadConfigFromArgs(args);

            Assert.Equal(audit, config.Audit.LogDirectory);
            Assert.False(Directory.Exists(hot), "配置解析仍是纯读，不得建出 hot 子树");
            Assert.False(Directory.Exists(audit));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Conflict_Audit_Should_Degrade_When_Existing_Audit_Directory_Is_Not_Writable()
    {
        // 防的回潮形态：删掉可写性探针后，「目录已存在但当前账户不可写」不再被识别——
        // Directory.CreateDirectory 原样返回，JsonLineAuditLogger 构造不抛，后台落盘静默丢行（#127）。
        // Windows 的目录 ReadOnly 属性不阻止建文件，ACL 改写与 CI 账户耦合，故该分支只在 POSIX 构造
        // （与 SingleWriterGateTests 的 symlink 用例同口径）；Windows 侧由实机只读目录验收覆盖。
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
            Assert.Empty(Directory.GetFiles(audit, "audit-*.jsonl"));
        }
        finally
        {
            File.SetUnixFileMode(audit, originalMode);
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

            File.Delete(path);
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

    private static string WaitForAuditFile(string auditDirectory, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var file = Directory.Exists(auditDirectory)
                ? Directory.GetFiles(auditDirectory, "audit-*.jsonl").FirstOrDefault()
                : null;
            if (!string.IsNullOrEmpty(file))
            {
                return file!;
            }

            Thread.Sleep(50);
        }

        throw new FileNotFoundException("instance_conflict audit file not found", auditDirectory);
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
