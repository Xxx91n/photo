using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.ExifTool;

public sealed class ExifToolBridge : IExifToolBridge, IDisposable
{
    private readonly IExifToolProcess _process;
    private readonly AppConfig _config;
    private readonly IReadOnlyDictionary<string, bool>? _wipeRules;
    private readonly ILogger<ExifToolBridge> _logger;
    private readonly Func<ExifToolLifecycleEvent, CancellationToken, ValueTask>? _lifecycleSink;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly SemaphoreSlim _stdinGate = new(1, 1);
    private readonly TimeSpan _startupTimeout;
    private static readonly TimeSpan RunningHealthTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 整行版本文本形态（<c>13.20</c> 或 <c>13.20 [Warning: …]</c>）。补探与在途擦除任务共享
    /// stdout，行内出现的文件名或日期不算版本——只有整行形态才可能是 <c>-ver</c> 输出。
    /// </summary>
    private static readonly Regex StandaloneVersionLine =
        new(@"^\s*\d+(?:\.\d+)+(?:\s*\[[^\]]*\])?\s*$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private volatile bool _started;
    private volatile bool _retired;
    private int _taskId;
    // 跨线程可见性：#86 的上报链路由 IPC 线程读取、stdout pump 线程写入。
    private volatile string _versionText = ExifToolVersionText.Unknown;

    public string VersionText => _versionText;

    public ExifToolBridge(IExifToolProcess process, AppConfig config, TimeSpan? healthTimeout = null)
        : this(process, config, NullLogger<ExifToolBridge>.Instance, lifecycleSink: null, healthTimeout)
    {
    }

    public ExifToolBridge(
        IExifToolProcess process,
        AppConfig config,
        ILogger<ExifToolBridge>? logger,
        Func<ExifToolLifecycleEvent, CancellationToken, ValueTask>? lifecycleSink = null,
        TimeSpan? healthTimeout = null,
        IReadOnlyDictionary<string, bool>? wipeRules = null)
    {
        _process = process;
        _config = config;
        _wipeRules = wipeRules;
        _logger = logger ?? NullLogger<ExifToolBridge>.Instance;
        _lifecycleSink = lifecycleSink;
        _startupTimeout = healthTimeout ?? TimeSpan.FromSeconds(3);
        _process.StdoutLine += OnStdoutLine;
    }

    public int PendingCount => _pending.Count;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
    }

    /// <summary>
    /// 停止并退役本桥：<b>终态</b>。StopAsync 之后该实例只会被 Dispose（停机或配置热重载），
    /// 不得再拉起进程——否则旧桥上会出现无人回收的子进程。
    ///
    /// 与启动路径互斥：先置 <see cref="_retired"/> 再取 <see cref="_startLock"/>。等在锁上的启动
    /// 路径重检终态后必然放弃；已进锁的启动路径则由本方法在锁内停掉它刚起的进程。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _retired = true;

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            _started = false;

            foreach (var marker in _pending.Keys)
            {
                if (_pending.TryRemove(marker, out var tcs))
                {
                    tcs.TrySetCanceled(cancellationToken);
                }
            }

            await _process.StopAsync(cancellationToken);
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>
    /// 确保 ExifTool 进程已启动。使用 SemaphoreSlim 防止并发启动竞争。
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        // 已退休的桥不得再拉起进程（见 StopAsync）：返回而不抛，调用方（文件路径/版本路径）
        // 各自按「拿不到进程」的既有方式收场。
        if (_retired)
        {
            return;
        }

        if (_started)
        {
            var health = await TryHealthCheckAsync(cancellationToken, RunningHealthTimeout);
            if (!health.IsHealthy)
            {
                await RestartAsync(cancellationToken, health);
            }
            return;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：获取锁后再次检查，避免重复启动。
            if (_started)
            {
                return;
            }

            await StartProcessLockedAsync(cancellationToken);
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>
    /// 只启动进程（必要时先清掉已死进程的句柄），<b>不做健康检查</b>——状态查询路径专用。
    ///
    /// 调用方必须持 <see cref="_startLock"/>。健康检查留在文件处理路径：
    /// 在忙进程上跑 500 ms 健康检查会超时并触发重启，那会打断在途擦除
    /// （其 marker 失去归属 → 输出可能被新进程的 {ready} 顶成「无元数据」而静默跳过该文件）。
    /// </summary>
    private async Task StartProcessLockedAsync(CancellationToken cancellationToken)
    {
        // 终态判定必须在持锁后重做：StopAsync 取同一把锁，因此「检查终态」与「创建子进程」
        // 之间插不进一次退役（ARCH-EXIFTOOL-RETIREMENT-001）。
        if (_retired)
        {
            return;
        }

        if (_started)
        {
            // 上次启动过但进程已不在（崩溃/被杀）：ProcessExifToolProcess.StartAsync 对非 null
            // 句柄是空操作，必须先 StopAsync 清句柄。
            await _process.StopAsync(cancellationToken);
            _started = false;
        }

        // 验证 ExifTool 路径在使用点仍然有效（防止热重载引入恶意路径）。
        ValidateExifToolPath(_config.ExifTool.Path);

        var startArgs = ExifToolCommandBuilder.BuildStartArguments(_config);

        await _process.StartAsync(
            _config.ExifTool.Path,
            startArgs,
            cancellationToken);

        _started = true;

        await EmitLifecycleEventAsync(
            new ExifToolLifecycleEvent(
                EventType: "exiftool_started",
                SourcePath: _config.ExifTool.Path,
                Message: "ExifTool stay_open process started.",
                Data: new Dictionary<string, string>
                {
                    ["exe_path"] = _config.ExifTool.Path,
                    ["arguments"] = string.Join(" ", startArgs)
                }),
            cancellationToken);

        await ProbeVersionAndWarnIfNeededAsync(cancellationToken);
    }

    /// <summary>
    /// 确保进程在跑，但绝不健康检查/重启（版本解析路径）。只在进程真的没了时启动它。
    /// </summary>
    // Design(#82): 状态查询只「拉起已死的进程」，不做健康检查——健康检查的副作用（重启）只属于文件路径。
    private async Task EnsureStartedWithoutHealthCheckAsync(CancellationToken cancellationToken)
    {
        if (_retired || _process.IsRunning)
        {
            return;
        }

        await _startLock.WaitAsync(cancellationToken);
        try
        {
            // 双重检查：等锁期间文件路径可能已经把进程拉起来了。
            if (_retired || _process.IsRunning)
            {
                return;
            }

            await StartProcessLockedAsync(cancellationToken);
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>
    /// 版本解析入口（#86）——只由状态消费者（IPC 状态请求）调用，<b>不进文件处理热路径</b>。
    ///
    /// 只有进程真的没了才启动它（启动自带版本探针）；进程还在就只补探。这条路径**绝不做健康检查**
    /// ——健康检查的副作用是重启，重启会打断在途擦除。已解析出真实版本时全程零往返。
    /// </summary>
    // Design(#82): 版本解析按需触发（IPC 状态请求驱动），既不做启动期 eager start，也不进每文件热路径。
    public async Task EnsureVersionAsync(CancellationToken cancellationToken)
    {
        if (_retired || !ExifToolVersionText.IsUnknown(_versionText))
        {
            return;
        }

        // 进程还在就不在这里做健康检查/重启：一次状态轮询不该把一个正在处理大文件、只是错过
        // 500 ms 健康应答的进程判成不健康并杀掉——重启会让在途擦除的 marker 失去归属，
        // 其输出可能被新进程的 {ready} 顶成「无元数据」而静默跳过该文件。
        // 进程真的没了（崩溃/被杀）时只把它拉起来，同样不健康检查：等锁期间擦除可能已把进程
        // 拉起并正在处理文件，走 EnsureStartedAsync 会在这里补一次健康检查。
        if (!_process.IsRunning)
        {
            await EnsureStartedWithoutHealthCheckAsync(cancellationToken);
            return;
        }

        // 补探与在途擦除任务共享 stdout：读侧靠「只接受整行版本文本」+ IsProtocolNoiseLine
        // 排除对方的协议 marker/JSON 行，写侧靠 WriteBlockAsync 保证命令块不被拆散。
        await ProbeVersionAndWarnIfNeededAsync(cancellationToken);
    }

    /// <summary>
    /// 整块写 stdin。stay_open 协议里一个命令块（含 <c>-execute</c> 收尾）必须连续写入：
    /// 四类写入（擦除探测块、擦除任务块、健康探针、版本探针）并发时会让一个块的行插进另一个块，
    /// 被对方的行处理器误读（例如把版本号拼进 JSON 探测输出 → 该文件被静默判为无元数据）。
    /// 块级串行化把这条前提钉住，代价是每次写多一次信号量往返。
    /// 例外：进程层 <c>ProcessExifToolProcess.StopAsync</c> 的 <c>-stay_open False</c> 收尾写字在
    /// 停机路径上直接写 stdin——那时进程正在被拆除，块完整性不再有约束意义。
    ///
    /// <paramref name="gateTimeoutToken"/> 只约束「取闸」这一步：健康/版本探针据此把排队等待计入
    /// 自己的超时预算（否则一次卡死的擦除写入会让 500 ms 健康预算永远等不到超时恢复）；
    /// 一旦拿到闸，写入本身仍只受 <paramref name="cancellationToken"/> 约束——半块写入会毒化协议流。
    /// </summary>
    private async Task WriteBlockAsync(
        string block,
        CancellationToken cancellationToken,
        CancellationToken gateTimeoutToken = default)
    {
        await _stdinGate.WaitAsync(gateTimeoutToken);
        try
        {
            await _process.WriteStdinAsync(block, cancellationToken);
        }
        finally
        {
            _stdinGate.Release();
        }
    }

    /// <summary>
    /// 清理单个文件的元数据：未知扩展名立即跳过；否则 stay_open 探测 → 判定是否有可清除字段 →
    /// 按规则生成擦除块并等待 <c>TASK_DONE_</c> marker。返回值即该文件的处理结论。
    /// </summary>
    public async Task<WipeResult> WipeMetadataAsync(string targetPath, CancellationToken cancellationToken)
    {
        // 未知扩展名一律立即跳过——不进 ExifTool、不写协议块、不注册等待 marker。
        // 必须早于 EnsureStartedAsync：不支持的格式不值得拉起进程，也不允许出现
        // “等一个永不出现的 TASK_DONE_ marker”的挂死路径。
        if (WipeStrategyResolver.ResolveFamily(targetPath) is WipeFormatFamily.Unknown)
        {
            _logger.LogWarning(
                "Skipping {Path}: extension is not in the wipe format family map; ExifTool was not invoked.",
                targetPath);
            return WipeResult.UnknownFormat;
        }

        await EnsureStartedAsync(cancellationToken);

        var id = Interlocked.Increment(ref _taskId).ToString();

        // Phase 1: Probe metadata via -json
        var probeMarker = $"PROBE_DONE_{id}";
        var probeTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var probeOutputBuilder = new System.Text.StringBuilder();
        var probeOutputLock = new object();

        // ExifTool stay_open protocol: -echo1 markers are echoed BEFORE file
        // processing output. So the order is:
        //   1. PROBE_DONE_{id}  (echo marker)
        //   2. [{...JSON...}]   (file metadata)
        //   3. {ready}           (task complete)
        // We use {ready} as the end-of-task delimiter.
        var markerSeen = false;
        void ProbeLineHandler(string line)
        {
            if (probeTcs.Task.IsCompleted)
                return;

            // 跳过 marker 行本身，只收集 marker 之后的数据。
            if (!markerSeen)
            {
                if (line.Contains(probeMarker, StringComparison.Ordinal))
                {
                    markerSeen = true;
                }
                return;
            }

            // {ready} prompt is ExifTool's end-of-task signal.
            if (line.StartsWith("{ready", StringComparison.Ordinal))
            {
                var result = probeOutputBuilder.ToString();
                probeTcs.TrySetResult(result);
                return;
            }

            // Collect lines between the echo marker and {ready}.
            lock (probeOutputLock)
            {
                probeOutputBuilder.AppendLine(line);
            }
        }

        _process.StdoutLine += ProbeLineHandler;
        try
        {
            var probeBlock = ExifToolCommandBuilder.BuildProbeTaskBlock(targetPath, id);
            await WriteBlockAsync(probeBlock, cancellationToken);
            await probeTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _process.StdoutLine -= ProbeLineHandler;
        }

        // Phase 2: Check probe result
        var probeOutputResult = probeTcs.Task.IsCompleted ? probeTcs.Task.Result : null;
        var hasClearableMetadata = !string.IsNullOrWhiteSpace(probeOutputResult) && HasClearableFields(probeOutputResult, targetPath);
        if (!hasClearableMetadata)
        {
            if (string.IsNullOrWhiteSpace(probeOutputResult))
            {
                _logger.LogDebug(
                    "Probe returned empty output for {Path}; treating as no metadata.",
                    targetPath);
                return WipeResult.Skipped_NoMetadata;
            }
            return WipeResult.Skipped_NoClearable;
        }

        // Phase 3: Wipe
        // 写盘前再解析一次策略。skip 情形（unknown_format / no_rules）绝不注册
        // 等待 marker——否则命令块里没有 TASK_DONE_，TCS 会挂到取消为止（原挂死链）。
        var wipeStrategy = WipeStrategyResolver.Resolve(targetPath, _wipeRules);
        if (wipeStrategy.SkipReason is not null)
        {
            _logger.LogWarning(
                "Skipping wipe for {Path}: wipe strategy skip reason {Reason}.",
                targetPath,
                wipeStrategy.SkipReason);

            return string.Equals(wipeStrategy.SkipReason, "unknown_format", StringComparison.Ordinal)
                ? WipeResult.UnknownFormat
                : WipeResult.Cleaned_NoOp;
        }

        var wipeMarker = $"TASK_DONE_{id}";
        var wipeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[wipeMarker] = wipeTcs;

        FileInfo? beforeInfo = null;
        long beforeLength = -1;
        DateTime beforeWriteTime = DateTime.MinValue;
        try
        {
            beforeInfo = new FileInfo(targetPath);
            beforeLength = beforeInfo.Length;
            beforeWriteTime = beforeInfo.LastWriteTimeUtc;
        }
        catch
        {
            // best-effort: post-check will be skipped
        }

        try
        {
            var wipeBlock = ExifToolCommandBuilder.BuildWipeTaskBlock(targetPath, id, _wipeRules);
            await WriteBlockAsync(wipeBlock, cancellationToken);
            await wipeTcs.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            _pending.TryRemove(wipeMarker, out _);
        }

        try
        {
            if (beforeInfo is not null)
            {
                beforeInfo.Refresh();
                if (beforeInfo.Exists
                    && beforeInfo.Length == beforeLength
                    && beforeInfo.LastWriteTimeUtc == beforeWriteTime)
                {
                    _logger.LogWarning("ExifTool wipe completed but file appears unchanged for {Path}.", targetPath);
                }
            }
        }
        catch
        {
            // best-effort post-check
        }

        return WipeResult.Cleaned_Modified;
    }

    /// <summary>
    /// 验证 ExifTool 路径的安全性。在进程启动和热重载时调用。
    /// </summary>
    private static void ValidateExifToolPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("ExifTool 路径不能为空");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"ExifTool 路径必须是绝对路径: {path}");
        }
    }

    private bool HasClearableFields(string jsonOutput, string targetPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return false;
            }

            var first = root[0];
            if (first.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            // 如果有超过 2 个字段（除了 SourceFile 和 File），说明有可清除的元数据。
            return first.EnumerateObject().Count() >= 2;
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse probe output for {Path}", targetPath);
            return false;
        }
    }

    /// <summary>
    /// 重启路径必须持 <see cref="_startLock"/>——与冷启动互斥。
    ///
    /// 原实现只有冷启动分支持锁，本方法完全无同步：多个调用方同时探测到 unhealthy 时会
    /// 并发 Stop/Start 同一进程（重启风暴），且 <c>_started</c> 标志被交错改写。
    /// 持锁后二次探测健康：若另一调用方已完成重启（进程已恢复），本调用不再重复 Stop/Start。
    /// </summary>
    private async Task RestartAsync(CancellationToken cancellationToken, HealthCheckResult reason)
    {
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            // 退休桥不再重启：StopAsync 取同一把锁，等锁期间发生退役时这里必须放弃，
            // 否则会在旧桥上留下一个无人回收的子进程（ARCH-EXIFTOOL-RETIREMENT-001）。
            if (_retired)
            {
                return;
            }

            if (_started)
            {
                var recheck = await TryHealthCheckAsync(cancellationToken, RunningHealthTimeout);
                if (recheck.IsHealthy)
                {
                    return;
                }

                reason = recheck;
            }

            _logger.LogWarning("Restarting ExifTool process: {Reason} - {Message}", reason.Reason, reason.Message);

            await EmitLifecycleEventAsync(
                new ExifToolLifecycleEvent(
                    EventType: "exiftool_restarted",
                    SourcePath: _config.ExifTool.Path,
                    Message: $"Restarting ExifTool: {reason.Reason} - {reason.Message}",
                    Data: reason.Data),
                cancellationToken);

            await _process.StopAsync(cancellationToken);
            _started = false;

            var startArgs = ExifToolCommandBuilder.BuildStartArguments(_config);
            await _process.StartAsync(_config.ExifTool.Path, startArgs, cancellationToken);
            _started = true;

            await ProbeVersionAndWarnIfNeededAsync(cancellationToken);
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task<HealthCheckResult> TryHealthCheckAsync(CancellationToken cancellationToken, TimeSpan timeout)
    {
        if (!_process.IsRunning)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "process_exited",
                Message: "ExifTool process is not running.",
                Data: BuildHealthFailureData("process_exited", "ExifTool process is not running."));
        }

        var marker = $"HEALTH_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[marker] = tcs;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            // Use explicit \n (not AppendLine/\r\n) — ExifTool stay_open protocol
            // requires LF-only line endings; \r\n breaks argument parsing.
            // 轻量存活探针——只做一次 stdin/stdout 往返（-echo1 marker + -execute），
            // 绝不把任何路径当「待解析文件」喂入。原实现把 _config.ExifTool.Path 当输入文件，
            // ExifTool 会真去解析那个（可能十几 MB 的）二进制，慢机上 >500ms 触发 health_timeout，
            // 于是每处理一个文件就整进程重启一次，摧毁 stay_open 常驻收益（不变量④）。
            var cmd = $"-fast\n-echo1\n{marker}\n-execute\n";

            // 取闸计入 500 ms 预算：闸被一次卡死的擦除写入长期占用时，这里必须超时走恢复，
            // 而不是无限排队（ARCH-EXIFTOOL-RETIREMENT 同源的准入缺口）。
            await WriteBlockAsync(cmd, cancellationToken, timeoutCts.Token);
            await tcs.Task.WaitAsync(timeoutCts.Token);
            return HealthCheckResult.Success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "health_timeout",
                Message: $"Health check timed out after {timeout.TotalMilliseconds}ms.",
                Data: BuildHealthFailureData("health_timeout", $"Timed out after {timeout.TotalMilliseconds}ms"));
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                IsHealthy: false,
                Reason: "health_error",
                Message: ex.Message,
                Data: BuildHealthFailureData("health_error", ex.Message));
        }
        finally
        {
            _pending.TryRemove(marker, out _);
        }
    }

    /// <summary>
    /// 探一次 <c>-ver</c> 并落 <see cref="_versionText"/>：只接受整行版本文本，顺带在已知
    /// WindowsLongPath 问题版本区间时发告警事件。超时/失败只记 warning，不改动已有版本值。
    /// </summary>
    private async Task ProbeVersionAndWarnIfNeededAsync(CancellationToken cancellationToken)
    {
        var marker = $"VERSION_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        string? capturedVersionLine = null;

        var markerSeen = false;
        void Handler(string line)
        {
            if (tcs.Task.IsCompleted) return;

            // 跳过 {ready} 和 VERSION_DONE_ 信号
            if (line.StartsWith("{ready", StringComparison.Ordinal)
                || line.StartsWith("VERSION_DONE_", StringComparison.Ordinal))
            {
                return;
            }

            // 本探针自己的 marker 必须先认领——它同样以 VERSION_ 开头。
            if (line.Contains(marker, StringComparison.Ordinal))
            {
                markerSeen = true;
                // 如果 version text 已经到达，立即返回结果
                if (capturedVersionLine is not null)
                {
                    tcs.TrySetResult(capturedVersionLine);
                }
                return;
            }

            // 补探（见 EnsureVersionAsync）与在途擦除任务共享 stdout：另一条命令块的协议 marker 行、
            // JSON 输出或「N image files updated」类状态行都不是版本号，先按前缀排除。
            if (IsProtocolNoiseLine(line))
            {
                return;
            }

            // 只接受整行版本文本。版本文本一旦被并发输出污染就被 latch 成永久版本
            // （IsUnknown 变 false，再也不重探），所以候选必须先过硬门槛。
            if (!string.IsNullOrWhiteSpace(line) && StandaloneVersionLine.IsMatch(line))
            {
                capturedVersionLine = line.Trim();
                // 如果 marker 已经看到，现在收到 version text，返回结果
                if (markerSeen)
                {
                    tcs.TrySetResult(capturedVersionLine);
                }
            }
        }

        _process.StdoutLine += Handler;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_startupTimeout);

            var cmd = $"-ver\n-echo1\n{marker}\n-execute\n";
            // 取闸同样计入探针预算：否则卡死的写入会让状态补探的 in-flight 门闩长期占住。
            await WriteBlockAsync(cmd, cancellationToken, timeoutCts.Token);

            var versionRaw = await tcs.Task.WaitAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(versionRaw))
            {
                _logger.LogWarning("ExifTool version probe returned no version text.");
                return;
            }

            var parsed = TryParseExifToolVersion(versionRaw);
            if (parsed is null)
            {
                _logger.LogWarning("Unable to parse ExifTool version from: {VersionText}", versionRaw);
                return;
            }

            _logger.LogInformation("Detected ExifTool version: {VersionText}", versionRaw);
            _versionText = versionRaw;

            if (_config.ExifTool.EnableWindowsLongPath && IsKnownWindowsLongPathIssueVersion(parsed))
            {
                var warningMessage =
                    $"ExifTool {parsed.ToString(2)} is in a known stay_open + WindowsLongPath issue range; consider upgrading ExifTool or disabling WindowsLongPath.";

                _logger.LogWarning(
                    "{WarningMessage}",
                    warningMessage);

                await EmitLifecycleEventAsync(
                    new ExifToolLifecycleEvent(
                        EventType: "exiftool_version_warning",
                        SourcePath: _config.ExifTool.Path,
                        Message: warningMessage,
                        Data: new Dictionary<string, string>
                        {
                            ["version"] = parsed.ToString(2)
                        }),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("ExifTool version probe timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ExifTool version probe failed.");
        }
        finally
        {
            _process.StdoutLine -= Handler;
        }
    }

    /// <summary>
    /// stay_open 协议噪声行——命令块边界 marker（任务/探测/健康/版本）或 JSON/提示输出，
    /// 它们永远不是版本文本。暖路径重探期间 stdout 与在途擦除任务共享，故须显式排除。
    /// </summary>
    private static bool IsProtocolNoiseLine(string line)
    {
        return line.StartsWith("TASK_DONE_", StringComparison.Ordinal)
            || line.StartsWith("PROBE_DONE_", StringComparison.Ordinal)
            || line.StartsWith("HEALTH_", StringComparison.Ordinal)
            || line.StartsWith("VERSION_", StringComparison.Ordinal)
            || line.StartsWith("[", StringComparison.Ordinal)
            || line.StartsWith("{", StringComparison.Ordinal);
    }

    private static Version? TryParseExifToolVersion(string versionText)
    {
        var match = Regex.Match(versionText, @"\d+(?:\.\d+)+");
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Value, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsKnownWindowsLongPathIssueVersion(Version version)
    {
        var minInclusive = new Version(13, 5);
        var maxExclusive = new Version(13, 6);
        return version >= minInclusive && version < maxExclusive;
    }

    private async ValueTask EmitLifecycleEventAsync(ExifToolLifecycleEvent lifecycleEvent, CancellationToken cancellationToken)
    {
        if (_lifecycleSink is null)
        {
            return;
        }

        try
        {
            await _lifecycleSink(lifecycleEvent, cancellationToken);
        }
        catch
        {
            // lifecycle auditing must never break bridge flow
        }
    }

    private Dictionary<string, string> BuildHealthFailureData(string reason, string detail)
    {
        var data = new Dictionary<string, string>
        {
            ["reason"] = reason,
            ["detail"] = detail,
            ["process_running"] = _process.IsRunning.ToString()
        };

        if (!string.IsNullOrWhiteSpace(_process.LastStderrLine))
        {
            data["stderr_last_line"] = _process.LastStderrLine!;
        }

        return data;
    }

    private sealed record HealthCheckResult(
        bool IsHealthy,
        string Reason,
        string Message,
        Dictionary<string, string>? Data)
    {
        public static readonly HealthCheckResult Success = new(
            IsHealthy: true,
            Reason: string.Empty,
            Message: string.Empty,
            Data: null);
    }

    private void OnStdoutLine(string line)
    {
        foreach (var marker in _pending.Keys)
        {
            if (!line.Contains(marker, StringComparison.Ordinal))
            {
                continue;
            }

            if (_pending.TryRemove(marker, out var tcs))
            {
                tcs.TrySetResult(true);
            }
        }
    }
    public void Dispose()
    {
        _startLock.Dispose();
        _stdinGate.Dispose();
    }

}


