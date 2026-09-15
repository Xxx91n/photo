using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Pipeline;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Pipeline;

/// <summary>
/// 票 03（A-006 / D-006 / 不变量②数据零意外丢失）：备份面镜像相对路径布局 + 冲突分流行为测试。
///
/// 原 bug：RuleEngine 备份槽位只取文件名——<c>hot/vacation/IMG_0001.jpg</c> 与
/// <c>hot/work/IMG_0001.jpg</c> 解析到同一 <c>{bak}/IMG_0001.jpg.bak</c>，
/// FileTaskPipeline 以 overwrite:true 静默互覆盖（后写覆盖前写，无提示）。
///
/// 三场景（issue Acceptance criteria #1）：
/// 1. 双子目录同名文件 → 两个备份都在且内容各自正确（镜像布局结构性消歧）；
/// 2. 同文件重处理（内容未变）→ 同槽位+内容相同跳过（承接"保留最新"合法语义，不产生旁路版本）；
/// 3. 同文件内容变化后重处理 → 写 名字.时间戳.扩展名 旁路版本，原槽位绝不覆盖。
///
/// 用真实 LocalFileOperations + 真实 FileTaskPipeline + 磁盘临时目录（非回放式 fake），
/// 断言的是盘上实物，不是调用记录。
/// </summary>
public sealed class BackupMirrorLayoutTests : IDisposable
{
    private readonly string _hotFolder;
    private readonly string _backupRoot;

    public BackupMirrorLayoutTests()
    {
        var seed = Guid.NewGuid().ToString("N")[..8];
        _hotFolder = Path.Combine(Path.GetTempPath(), "pp-bml-hot-" + seed);
        _backupRoot = Path.Combine(_hotFolder, "bak");
        Directory.CreateDirectory(Path.Combine(_hotFolder, "vacation"));
        Directory.CreateDirectory(Path.Combine(_hotFolder, "work"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_hotFolder, recursive: true); } catch { /* best-effort */ }
    }

    // ── 场景 1：双子目录同名 → 两个备份都在且内容各自正确 ──────────────────

    [Fact]
    public async Task HandleAsync_TwinNames_In_DifferentSubdirs_Should_Produce_Two_Distinct_Backups()
    {
        var vacation = Path.Combine(_hotFolder, "vacation", "IMG_0001.jpg");
        var work = Path.Combine(_hotFolder, "work", "IMG_0001.jpg");
        File.WriteAllBytes(vacation, [0xFF, 0xD8, 0xAA, 0x01]);
        File.WriteAllBytes(work, [0xFF, 0xD8, 0xBB, 0x02]);

        var audit = new InMemoryAuditLogger();
        // 每次运行用独立 processedStore：隔离"已处理跳过"路径，聚焦备份冲突分流本身。
        await BuildPipeline(audit).HandleAsync(vacation, CancellationToken.None);
        await BuildPipeline(audit).HandleAsync(work, CancellationToken.None);

        var vacationBackup = Path.Combine(_backupRoot, "vacation", "IMG_0001.jpg.bak");
        var workBackup = Path.Combine(_backupRoot, "work", "IMG_0001.jpg.bak");

        Assert.True(File.Exists(vacationBackup), $"镜像槽位缺失：{vacationBackup}");
        Assert.True(File.Exists(workBackup), $"镜像槽位缺失：{workBackup}");
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xAA, 0x01 }, File.ReadAllBytes(vacationBackup));
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xBB, 0x02 }, File.ReadAllBytes(workBackup));

        // 备份面全景：恰两个 .bak，无任何文件落在扁平根（旧布局碰撞槽 {bak}/IMG_0001.jpg.bak 不存在）。
        var allBackups = Directory.EnumerateFiles(_backupRoot, "*.bak", SearchOption.AllDirectories).ToList();
        Assert.Equal(2, allBackups.Count);
        Assert.False(File.Exists(Path.Combine(_backupRoot, "IMG_0001.jpg.bak")));
        Assert.DoesNotContain(audit.Events, e => e.EventType == "backup_sidecar_versioned");
    }

    // ── 场景 2：同文件重处理（内容相同）→ 跳过，语义保留 ──────────────────

    [Fact]
    public async Task HandleAsync_SameFile_Reprocessed_With_Identical_Content_Should_Skip_Without_Sidecar()
    {
        var source = Path.Combine(_hotFolder, "vacation", "reprocess.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xCC, 0x03]);

        var audit = new InMemoryAuditLogger();
        await BuildPipeline(audit).HandleAsync(source, CancellationToken.None);

        var slot = Path.Combine(_backupRoot, "vacation", "reprocess.jpg.bak");
        Assert.True(File.Exists(slot));

        // 重处理（fresh store 绕开 already_processed 短路，直击冲突分流）：内容相同 → 跳过。
        await BuildPipeline(audit).HandleAsync(source, CancellationToken.None);

        Assert.Contains(audit.Events, e =>
            e.EventType == "backup_skipped" && e.Message == "identical_content_slot_kept");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "backup_sidecar_versioned");

        // 盘上仍恰一个备份、内容不变（合法语义"同文件重处理保留最新"由跳过承接）。
        var allBackups = Directory.EnumerateFiles(_backupRoot, "*.bak", SearchOption.AllDirectories).ToList();
        Assert.Single(allBackups);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xCC, 0x03 }, File.ReadAllBytes(slot));
    }

    // ── 场景 3：内容变化 → 旁路版本，绝不静默覆盖 ─────────────────────────

    [Fact]
    public async Task HandleAsync_Content_Changed_Should_Write_Sidecar_And_Keep_Original_Slot()
    {
        var source = Path.Combine(_hotFolder, "work", "diverge.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xDD, 0x04]);

        var audit = new InMemoryAuditLogger();
        await BuildPipeline(audit).HandleAsync(source, CancellationToken.None);

        var slot = Path.Combine(_backupRoot, "work", "diverge.jpg.bak");
        var slotBytesV1 = File.ReadAllBytes(slot);

        // 源内容变化（模拟"两次清理之间原始文件已不同"）。
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xEE, 0x05, 0x06]);

        await BuildPipeline(audit).HandleAsync(source, CancellationToken.None);

        // 原槽位绝不覆盖：仍是第一次的真原始。
        Assert.Equal(slotBytesV1, File.ReadAllBytes(slot));

        // 旁路版本产生：名字.时间戳.扩展名，内容为第二次源。
        var sidecarEvent = audit.Events.Single(e => e.EventType == "backup_sidecar_versioned");
        var sidecar = sidecarEvent.Data!["sidecar"];
        Assert.StartsWith(Path.Combine(_backupRoot, "work", "diverge.jpg."), sidecar);
        Assert.Matches(@"diverge\.jpg\.\d{8}-\d{6}(-\d+)?\.bak$", sidecar);
        Assert.True(File.Exists(sidecar));
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xEE, 0x05, 0x06 }, File.ReadAllBytes(sidecar));

        var allBackups = Directory.EnumerateFiles(_backupRoot, "*.bak", SearchOption.AllDirectories).ToList();
        Assert.Equal(2, allBackups.Count);
    }

    // ── 路径构造不变量（Acceptance criteria #2）：不存在不同源文件同槽位 ────

    [Fact]
    public void ResolveBackupSlot_CrossSubdirSameName_Should_Never_Collide()
    {
        var hot = Path.Combine(Path.GetTempPath(), "pp-rbs-hot");
        var slotA = BackupPathResolver.ResolveBackupSlot(
            Path.Combine(hot, "vacation", "IMG_0001.jpg"), hot, Path.Combine(hot, "bak"), ".bak");
        var slotB = BackupPathResolver.ResolveBackupSlot(
            Path.Combine(hot, "work", "IMG_0001.jpg"), hot, Path.Combine(hot, "bak"), ".bak");

        Assert.NotNull(slotA);
        Assert.NotNull(slotB);
        Assert.NotEqual(slotA, slotB);
        Assert.Equal(Path.Combine(hot, "bak", "vacation", "IMG_0001.jpg.bak"), slotA);
        Assert.Equal(Path.Combine(hot, "bak", "work", "IMG_0001.jpg.bak"), slotB);
    }

    [Fact]
    public void ResolveBackupSlot_SiblingPrefix_Trap_Should_Fall_Back_To_Unsorted()
    {
        // "/a/repo" vs "/a/repo-backup" 兄弟目录陷阱：源不在监控根子树内 → 兜底 _unsorted + 短哈希。
        var hot = Path.Combine(Path.GetTempPath(), "pp-rbs-root");
        var outside = Path.Combine(Path.GetTempPath(), "pp-rbs-root-backup", "IMG_0002.jpg");

        var slot = BackupPathResolver.ResolveBackupSlot(outside, hot, Path.Combine(hot, "bak"), ".bak");

        Assert.NotNull(slot);
        Assert.StartsWith(Path.Combine(hot, "bak", BackupPathResolver.FallbackDirName), slot!);
        Assert.Contains("IMG_0002.jpg", slot);
    }

    [Fact]
    public void ResolveBackupSlot_Should_Normalize_Reserved_Windows_Names()
    {
        var hot = Path.Combine(Path.GetTempPath(), "pp-rbs-res");
        var slot = BackupPathResolver.ResolveBackupSlot(
            Path.Combine(hot, "CON.jpg"), hot, Path.Combine(hot, "bak"), ".bak");

        Assert.NotNull(slot);
        Assert.EndsWith("_CON.jpg.bak", slot);
    }

    [Fact]
    public void ResolveSidecarPath_Should_Insert_Timestamp_Before_Suffix_And_Serial_On_Collision()
    {
        var slot = Path.Combine("D:", "bak", "work", "IMG.jpg.bak");
        var ts = new DateTimeOffset(2026, 9, 16, 2, 15, 30, TimeSpan.Zero);

        var first = BackupPathResolver.ResolveSidecarPath(slot, ".bak", ts, exists: _ => false);
        Assert.Equal(Path.Combine("D:", "bak", "work", "IMG.jpg.20260916-021530.bak"), first);

        // 同秒碰撞（首名已存在）→ 追加 -N 序号。
        var second = BackupPathResolver.ResolveSidecarPath(slot, ".bak", ts, exists: p => p == first);
        Assert.Equal(Path.Combine("D:", "bak", "work", "IMG.jpg.20260916-021530-1.bak"), second);
    }

    // ── retention 兜底：镜像子树递归可见 + 空目录清理（票 03 新增语义锁定）──

    [Fact]
    public void EnforceMaxSize_Should_See_Mirror_Subtree_Backups_And_Cleanup_Empty_Dirs()
    {
        var nested = Path.Combine(_backupRoot, "vacation", "2026");
        Directory.CreateDirectory(nested);
        var empty = Path.Combine(_backupRoot, "work", "old-empty");
        Directory.CreateDirectory(empty);
        File.WriteAllBytes(Path.Combine(nested, "IMG.jpg.bak"), new byte[10]);
        File.WriteAllBytes(Path.Combine(nested, "IMG.jpg.20260916-021530.bak"), new byte[10]);

        // 上限远大于总量：不删任何文件（锁定"递归枚举不误删"）；
        // 空目录清理自底向上移除 work/old-empty 与空父目录，保留有文件的 vacation 链。
        BackupRetentionService.EnforceMaxSize(_backupRoot, maxSizeBytes: 1024 * 1024, retainDays: 0);

        Assert.True(File.Exists(Path.Combine(nested, "IMG.jpg.bak")));
        Assert.True(File.Exists(Path.Combine(nested, "IMG.jpg.20260916-021530.bak")));
        Assert.False(Directory.Exists(empty));

        // 超上限：镜像子树内的旁路版本也必须能被 size 兜底清掉（AllDirectories 生效证明）。
        var all = new List<string>
        {
            Path.Combine(_backupRoot, "vacation", "IMG.jpg.bak"),
            Path.Combine(_backupRoot, "vacation", "IMG.jpg.20260916-021530.bak"),
        };
        foreach (var p in all)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[600]);
        }

        BackupRetentionService.EnforceMaxSize(_backupRoot, maxSizeBytes: 1024, retainDays: 0);
        var survivors = Directory.EnumerateFiles(_backupRoot, "*.bak", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .ToList();
        // 锁定语义 = 递归枚举让 size 兜底对镜像子树生效（删除顺序按最旧优先，不断言幸存个数）。
        Assert.True(survivors.Sum(f => f.Length) <= 1024,
            $"size 兜底未递归生效：{string.Join("; ", survivors.Select(f => $"{f.Name}:{f.Length}"))}");
        Assert.NotEmpty(survivors); // 远未超限时不误删光
    }

    private FileTaskPipeline BuildPipeline(InMemoryAuditLogger audit)
    {
        var config = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with { AllowedExtensions = [".jpg"] },
            Watch = AppConfig.Default.Watch with { HotFolder = _hotFolder },
            Backup = AppConfig.Default.Backup with { Enabled = true, Directory = _backupRoot, Suffix = ".bak" },
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Enabled = false }
        };

        return new FileTaskPipeline(
            config,
            new RuleEngine(config),
            new AlwaysModifiedBridge(),
            new LocalFileOperations(),
            audit,
            new InMemoryProcessedRecordStore());
    }

    private sealed class InMemoryAuditLogger : IAuditLogger
    {
        public List<AuditEvent> Events { get; } = [];

        public ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>返回 Cleaned_Modified 且不触碰目标文件——备份内容即清理前源内容。</summary>
    private sealed class AlwaysModifiedBridge : IExifToolBridge
    {
        public string VersionText => "fake-12.00";

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
            => Task.FromResult(WipeResult.Cleaned_Modified);
    }
}
