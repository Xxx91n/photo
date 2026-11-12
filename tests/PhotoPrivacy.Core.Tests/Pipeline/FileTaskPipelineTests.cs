using PhotoPrivacy.Core.Audit;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;
using PhotoPrivacy.Core.Pipeline;
using PhotoPrivacy.Core.Rules;

namespace PhotoPrivacy.Core.Tests.Pipeline;

public sealed class FileTaskPipelineTests
{
    [Fact]
    public async Task HandleAsync_Should_Move_To_Quarantine_After_Max_Retries()
    {
        // 返修：隔离子、拼接断言平台中立（原 D:\ 字面量在 Linux 与 Path.Combine 结果不匹配）
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 2, BackoffSeconds = [0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "a.jpg"), CancellationToken.None);

        Assert.Contains(fileOps.Moves, m => m.Source == Path.Combine(hot, "a.jpg") && m.Destination == Path.Combine(quarantine, "a.jpg"));
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_And_Quarantine_Without_Retry_When_Bridge_Reports_Write_Error()
    {
        // #288：ExifTool 明确写不了（分片 MP4 即此形态）时，审计必须是 file_processing_failed，
        // 绝不许 file_processing_succeeded；确定性错误重试必然同样失败，不进重试循环，
        // 直接按设置进隔离区，且不写 processed 记录（下轮扫描不再走已处理挡板）。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 3, BackoffSeconds = [0, 0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "fragmented.mp4"), CancellationToken.None);

        Assert.Equal(1, bridge.Calls);
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_retry_scheduled");
        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_processing_failed"
                && e.Message.Contains("wipe_result=Failed_WriteError", StringComparison.Ordinal));
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
        Assert.Contains(
            fileOps.Moves,
            m => m.Source == Path.Combine(hot, "fragmented.mp4")
                && m.Destination == Path.Combine(quarantine, "fragmented.mp4"));
    }

    [Fact]
    public async Task HandleAsync_Should_Not_Enter_Retry_And_Report_Quarantine_Failure_Separately_When_Move_Fails()
    {
        // CodeRabbit #294 意见 1：隔离目录不可写/Move 失败时，异常不许掉进擦除重试的 catch
        // ——写错误是确定性的，重试必然同样失败；且移动成功后重试还会再碰已移走的文件。
        // 隔离是终态收场，失败单独落 file_quarantine_failed（Error），擦除结论保持不变。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 3, BackoffSeconds = [0, 0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations { ThrowOnMove = true };
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "a.mp4"), CancellationToken.None);

        Assert.Equal(1, bridge.Calls);
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_retry_scheduled");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_processing_failed"
                && e.Message.Contains("wipe_result=Failed_WriteError", StringComparison.Ordinal));
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_quarantined");
        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_quarantine_failed"
                && e.Message.Contains("quarantine_target=", StringComparison.Ordinal));
        Assert.Empty(fileOps.Moves);
    }

    [Fact]
    public async Task HandleAsync_Should_Quarantine_Same_Name_To_Sidecar_Without_Overwriting_Previous_Original()
    {
        // CodeRabbit #294 意见 2：LocalFileOperations.Move 目标存在会先删后移——同名文件先后
        // 隔离时，第二次会把第一次隔离的原件删掉，而写错误分支没有备份，丢了就没了。
        // 隔离槽位被占时必须按侧车模式另存（名字.时间戳.扩展名），绝不覆盖。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-sidecar-" + Guid.NewGuid().ToString("N"));
        var quarantine = Path.Combine(hot, "_quarantine");
        Directory.CreateDirectory(hot);
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        try
        {
            var ruleEngine = new RuleEngine(cfg);
            var bridge = new WriteErrorBridge();
            var fileOps = new LocalFileOperations();
            var audit = new InMemoryAuditLogger();
            var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

            var file = Path.Combine(hot, "clip.mp4");
            File.WriteAllBytes(file, [1, 2, 3]);
            await pipeline.HandleAsync(file, CancellationToken.None);

            var originalSlot = Path.Combine(quarantine, "clip.mp4");
            Assert.True(File.Exists(originalSlot), "第一次隔离应占用原名槽位");

            File.WriteAllBytes(file, [4, 5, 6]);
            await pipeline.HandleAsync(file, CancellationToken.None);

            Assert.True(File.Exists(originalSlot), "同名第二次隔离绝不许删掉第一次隔离的原件");
            var quarantined = Directory.GetFiles(quarantine);
            Assert.Equal(2, quarantined.Length);
            var sidecar = quarantined.Single(p => !string.Equals(p, originalSlot, StringComparison.OrdinalIgnoreCase));
            var sidecarName = Path.GetFileName(sidecar);
            Assert.StartsWith("clip.", sidecarName, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".mp4", sidecarName, StringComparison.OrdinalIgnoreCase);
            // 第二次隔离的是重建后的 [4,5,6]，不是第一次的 [1,2,3]——sidecar 装的是新原件。
            Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(sidecar));
        }
        finally
        {
            Directory.Delete(hot, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_Should_Keep_Every_Original_When_Same_Name_Files_Quarantine_Concurrently()
    {
        // CodeRabbit #294 意见 3：不同源路径的 HandleAsync 可并发（_inflight 按源路径去重，
        // 挡不住同名目标），若「File.Exists 选名」与「Move」不同锁，两侧并发都会选中同一隔离
        // 目标，后一次 Move 先删后移把先前隔离的原件删掉——写错误分支无备份，丢了就没了。
        // 选名+Move 串行化后，N 份同名原件必须全部保留、互不覆盖。
        var root = Path.Combine(Path.GetTempPath(), "pp-ft-concurrent-" + Guid.NewGuid().ToString("N"));
        var quarantine = Path.Combine(root, "_quarantine");
        Directory.CreateDirectory(root);
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        try
        {
            var ruleEngine = new RuleEngine(cfg);
            var bridge = new WriteErrorBridge();
            var fileOps = new LocalFileOperations();
            var audit = new InMemoryAuditLogger();
            var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

            const int fileCount = 6;
            var files = new string[fileCount];
            var contents = new byte[fileCount][];
            for (var i = 0; i < fileCount; i++)
            {
                var dir = Path.Combine(root, "src-" + i);
                Directory.CreateDirectory(dir);
                files[i] = Path.Combine(dir, "clip.mp4");
                contents[i] = [(byte)i, (byte)(i + 1), (byte)(i + 2)];
                File.WriteAllBytes(files[i], contents[i]);
            }

            // 假件全是同步完成，直接 for 里调 HandleAsync 会顺序跑完、锁从不见争用——
            // 用 Barrier 让六次调用同一时刻才放行，真正并发进 QuarantineAsync（CodeRabbit #294 nitpick）。
            using var startGate = new Barrier(fileCount);
            var tasks = files.Select(file => Task.Run(async () =>
            {
                startGate.SignalAndWait();
                await pipeline.HandleAsync(file, CancellationToken.None);
            })).ToArray();

            await Task.WhenAll(tasks);

            // 六份同名原件都要在隔离区，内容逐一保留，一个都不能少。
            var quarantined = Directory.GetFiles(quarantine, "clip*.mp4");
            Assert.Equal(fileCount, quarantined.Length);
            var contentsInQuarantine = new List<byte[]>();
            foreach (var path in quarantined)
            {
                contentsInQuarantine.Add(await File.ReadAllBytesAsync(path));
            }

            foreach (var expected in contents)
            {
                Assert.Contains(
                    contentsInQuarantine,
                    actual => actual.Length == expected.Length
                        && actual.Zip(expected, (a, b) => a == b).All(x => x));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HandleAsync_Should_Still_Quarantine_When_Failure_Audit_Write_Throws()
    {
        // CodeRabbit #294 意见 1 追评 A：审计通道失效（file_processing_failed 写抛）不得回灌进
        // 擦除重试，也不得连坐数据安全动作——结论已定（失败），重擦无意义，隔离（数据安全）照做。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 3, BackoffSeconds = [0, 0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        audit.ThrowOnEventTypes.Add("file_processing_failed");
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "a.mp4"), CancellationToken.None);

        Assert.Equal(1, bridge.Calls);
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_retry_scheduled");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.Contains(
            fileOps.Moves,
            m => m.Source == Path.Combine(hot, "a.mp4") && m.Destination == Path.Combine(quarantine, "a.mp4"));
    }

    [Fact]
    public async Task HandleAsync_Should_Not_Report_Quarantine_Failed_When_Only_PostMove_Audit_Throws()
    {
        // CodeRabbit #294 意见 1 追评 B：Move 成功后 file_quarantined 审计写失败——文件其实
        // 已安全隔离，绝不许谎报 file_quarantine_failed（事件缺席≠隔离失败），也不许回灌重试。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var quarantine = Path.Combine(hot, "_quarantine");
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 3, BackoffSeconds = [0, 0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        audit.ThrowOnEventTypes.Add("file_quarantined");
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "a.mp4"), CancellationToken.None);

        Assert.Equal(1, bridge.Calls);
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_retry_scheduled");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_quarantine_failed");
        Assert.Contains(
            fileOps.Moves,
            m => m.Source == Path.Combine(hot, "a.mp4") && m.Destination == Path.Combine(quarantine, "a.mp4"));
    }

    [Fact]
    public async Task HandleAsync_Should_Propagate_Cancellation_Without_Misrecorded_Failure_Events()
    {
        // CodeRabbit #294 意见 1 追评 C：关停/取消不是失败——擦除中途取消（OCE）不得误记
        // file_processing_failed / file_retry_scheduled，取消语义原样上抛。
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 3, BackoffSeconds = [0, 0, 0] }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new CanceledBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.HandleAsync(@"D:\hot\a.mp4", CancellationToken.None));

        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_failed");
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_retry_scheduled");
        Assert.Empty(fileOps.Moves);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_Without_Quarantine_When_Quarantine_Disabled()
    {
        // #288：隔离未启用时不挪文件（也无隔离事件），但失败审计照写——用户至少看得到失败。
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Enabled = false }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new WriteErrorBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(@"D:\hot\a.mp4", CancellationToken.None);

        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_processing_succeeded");
        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_processing_failed"
                && e.Message.Contains("wipe_result=Failed_WriteError", StringComparison.Ordinal));
        Assert.DoesNotContain(audit.Events, e => e.EventType == "file_quarantined");
        Assert.Empty(fileOps.Moves);
    }

    [Fact]
    public async Task HandleAsync_Should_Skip_When_File_Is_InFlight()
    {
        var cfg = AppConfig.Default;
        var ruleEngine = new RuleEngine(cfg);
        var bridge = new SlowSuccessBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        var t1 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);
        var t2 = pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        await Task.WhenAll(t1, t2);
        Assert.Equal(1, bridge.Calls);
    }

    [Fact]
    public async Task HandleAsync_Should_Copy_To_Fixed_Output_Before_Wiping()
    {
        // 返修：fixed_directory 拼接 = Combine(outputDirectory, relative)——平台中立构造
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var clean = Path.Combine(Path.GetTempPath(), "pp-ft-clean");
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = clean
            },
            Watch = AppConfig.Default.Watch with { HotFolder = hot },
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new CaptureTargetBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "album", "a.jpg"), CancellationToken.None);

        Assert.Contains(fileOps.Copies, c => c.Source == Path.Combine(hot, "album", "a.jpg") && c.Destination == Path.Combine(clean, "album", "a.jpg"));
        Assert.Equal(Path.Combine(clean, "album", "a.jpg"), bridge.LastTargetPath);
    }

    [Fact]
    public async Task HandleAsync_Should_Write_FileDetected_And_FileProcessingStarted_Before_Success()
    {
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new CaptureTargetBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        var eventTypes = audit.Events.Select(x => x.EventType).ToList();
        var detectedIndex = eventTypes.IndexOf("file_detected");
        var startedIndex = eventTypes.IndexOf("file_processing_started");
        var succeededIndex = eventTypes.IndexOf("file_processing_succeeded");

        Assert.True(detectedIndex >= 0, "missing file_detected");
        Assert.True(startedIndex >= 0, "missing file_processing_started");
        Assert.True(succeededIndex >= 0, "missing file_processing_succeeded");
        Assert.True(detectedIndex < startedIndex, "file_detected should appear before file_processing_started");
        Assert.True(startedIndex < succeededIndex, "file_processing_started should appear before file_processing_succeeded");
    }

    [Fact]
    public async Task HandleAsync_Should_Move_Output_File_To_Quarantine_When_Retry_Exhausted_And_Output_Is_Fixed()
    {
        // 返修：隔离 quarantine 拼接 = Combine(directory, fileName)——平台中立构造
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-hot");
        var clean = Path.Combine(Path.GetTempPath(), "pp-ft-clean");
        var quarantine = Path.Combine(Path.GetTempPath(), "pp-ft-quar");
        var cfg = AppConfig.Default with
        {
            Rules = AppConfig.Default.Rules with
            {
                OutputMode = "fixed_directory",
                OutputDirectory = clean
            },
            Watch = AppConfig.Default.Watch with { HotFolder = hot },
            Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
            Quarantine = AppConfig.Default.Quarantine with { Enabled = true, Directory = quarantine }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(Path.Combine(hot, "album", "a.jpg"), CancellationToken.None);

        Assert.Contains(fileOps.Moves, m => m.Source == Path.Combine(clean, "album", "a.jpg") && m.Destination == Path.Combine(quarantine, "a.jpg"));
        Assert.Contains(audit.Events, e => e.EventType == "file_quarantined");
    }

    [Fact]
    public async Task HandleAsync_Should_Write_FileRetryScheduled_Before_Second_Attempt()
    {
        var cfg = AppConfig.Default with
        {
            Retry = AppConfig.Default.Retry with { MaxAttempts = 2, BackoffSeconds = [0, 0] },
            Quarantine = AppConfig.Default.Quarantine with { Directory = @"D:\hot\_quarantine" }
        };

        var ruleEngine = new RuleEngine(cfg);
        var bridge = new AlwaysFailBridge();
        var fileOps = new InMemoryFileOperations();
        var audit = new InMemoryAuditLogger();
        var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, new InMemoryProcessedRecordStore());

        await pipeline.HandleAsync(@"D:\hot\a.jpg", CancellationToken.None);

        Assert.Contains(
            audit.Events,
            e => e.EventType == "file_retry_scheduled"
                && e.Message.Contains("next_attempt=2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_Should_Skip_Rescan_Of_Same_Path_After_Content_Changed()
    {
        // 要防的老毛病：same_as_source 清洗后文件长度/mtime 指纹变化，旧去重键（路径|长度|mtime）
        // 不再命中，整目录补漏扫把已清洗文件又处理一遍（#133）。
        // 关联 issue #133。
        // 路线 2 规则：去重键收敛为规范化路径粗粒度命中——同一路径已有记录即视为已处理，不再比对指纹。
        var hot = Path.Combine(Path.GetTempPath(), "pp-ft-rescan-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(hot);
        var file = Path.Combine(hot, "a.jpg");
        File.WriteAllBytes(file, [1, 2, 3]);

        try
        {
            var cfg = AppConfig.Default with
            {
                Retry = AppConfig.Default.Retry with { MaxAttempts = 1, BackoffSeconds = [0] },
                Backup = AppConfig.Default.Backup with { Enabled = false }
            };
            var ruleEngine = new RuleEngine(cfg);
            var bridge = new SlowSuccessBridge();
            var fileOps = new InMemoryFileOperations();
            var audit = new InMemoryAuditLogger();
            var store = new InMemoryProcessedRecordStore();
            var pipeline = new FileTaskPipeline(cfg, ruleEngine, bridge, fileOps, audit, store);

            await pipeline.HandleAsync(file, CancellationToken.None);
            Assert.Equal(1, bridge.Calls);

            // 模拟清洗后文件内容/指纹变化（same_as_source 会改长度和 mtime）：补漏扫同一路径应被路径命中挡住。
            File.WriteAllBytes(file, [9, 9, 9, 9, 9]);

            await pipeline.HandleAsync(file, CancellationToken.None);

            Assert.Equal(1, bridge.Calls);
            Assert.Contains(audit.Events, e => e.EventType == "file_skipped" && e.Message == "already_processed");
        }
        finally
        {
            Directory.Delete(hot, recursive: true);
        }
    }

    private sealed class InMemoryFileOperations : IFileOperations
    {
        // 并发用例（Barrier 放行）会同时写这些集合，加锁防 List 内部状态被打坏。
        private readonly object _lock = new();

        public List<(string Source, string Destination)> Moves { get; } = [];
        public List<(string Source, string Destination, bool Overwrite)> Copies { get; } = [];
        public List<string> EnsuredDirectories { get; } = [];

        /// <summary>故障注入：Move 一律抛（模拟隔离目录不可写），用于钉「隔离失败不回灌重试」。</summary>
        public bool ThrowOnMove { get; set; }

        public void EnsureDirectory(string path)
        {
            lock (_lock)
            {
                EnsuredDirectories.Add(path);
            }
        }

        public void Move(string source, string destination)
        {
            if (ThrowOnMove)
            {
                throw new IOException("quarantine directory is not writable");
            }

            lock (_lock)
            {
                Moves.Add((source, destination));
            }
        }

        public void Copy(string source, string destination, bool overwrite)
        {
            lock (_lock)
            {
                Copies.Add((source, destination, overwrite));
            }
        }

        public void AtomicCopy(string source, string destination, bool overwrite)
        {
            lock (_lock)
            {
                Copies.Add((source, destination, overwrite));
            }
        }

        public Task CopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Copies.Add((source, destination, overwrite));
            }

            return Task.CompletedTask;
        }

        public Task AtomicCopyAsync(string source, string destination, bool overwrite, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Copies.Add((source, destination, overwrite));
            }

            return Task.CompletedTask;
        }

        public Task MoveAsync(string source, string destination, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Moves.Add((source, destination));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryAuditLogger : IAuditLogger
    {
        private readonly object _lock = new();

        public List<AuditEvent> Events { get; } = [];

        /// <summary>故障注入：写这些事件类型时抛（模拟审计通道失效），用于钉「审计失败不回灌重试、不谎报隔离失败」。</summary>
        public HashSet<string> ThrowOnEventTypes { get; } = new(StringComparer.Ordinal);

        public ValueTask WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            if (ThrowOnEventTypes.Contains(auditEvent.EventType))
            {
                throw new IOException("audit sink unavailable");
            }

            lock (_lock)
            {
                Events.Add(auditEvent);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class AlwaysFailBridge : IExifToolBridge
    {
        public string VersionText => "test";

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("fail");
        }
    }

    private sealed class WriteErrorBridge : IExifToolBridge
    {
        public string VersionText => "test";

        private int _calls;

        /// <summary>并发用例会同时调 WipeMetadataAsync，计数用 Interlocked 防丢。</summary>
        public int Calls => Volatile.Read(ref _calls);

        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(WipeResult.Failed_WriteError);
        }
    }

    private sealed class CanceledBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            throw new OperationCanceledException("shutting down mid-wipe");
        }
    }

    private sealed class CaptureTargetBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public string LastTargetPath { get; private set; } = string.Empty;

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            LastTargetPath = targetPath;
            return Task.FromResult(WipeResult.Cleaned_Modified);
        }
    }

    private sealed class SlowSuccessBridge : IExifToolBridge
    {
        public string VersionText => "test";

        public int Calls { get; private set; }

        /// <summary>版本解析在假件里无副作用：版本由构造参数直接给出。</summary>
        public Task EnsureVersionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(50, cancellationToken);
            return WipeResult.Cleaned_Modified;
        }
    }
}
