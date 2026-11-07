using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

/// <summary>
/// 块产出可延后的 stay_open 假件——按参数语义产出（<c>-echo1</c> 回显 → <c>-json</c> 数据 → <c>{ready}</c>），
/// 但**带文件参数的块**（探测 / 擦除）写入后不立刻产出，等用例逐块、逐段放行。
///
/// 为什么不复用 <see cref="ProtocolLevelFakeExifToolProcess"/>：它把「一块的回显到 <c>{ready}</c> 之间
/// 插不进别块的行」当作回放前提（单线程块处理队列），那正是 #226 已真进程实测覆盖的假设（原登记 #39，StayOpenConcurrentBlockTests）。要锁死
/// 「marker 互为前缀时归属不串块」（#111），必须能注入该假设<b>之外</b>的时序，故本假件把块产出时机交给用例。
///
/// 交给用例的只有时序，语义仍是协议的：回显文本取自命令块里的 <c>-echo1</c>（不认识任何 marker 名字、
/// 也没有「是否发射 SUT 期待的那一行」的开关），只有 <c>-json</c> 块产出探测 JSON，块结束信号恒为
/// <c>{ready}</c>。健康 / 版本探针不含文件参数，照常同步产出——否则冷启动与每文件健康检查会把用例自己挂死。
/// </summary>
internal sealed class DeferredBlockFakeExifToolProcess : IExifToolProcess
{
    /// <summary>-json 探测输出：带可清除字段的元数据数组（与协议级假件同一形态）。</summary>
    private const string ProbeJson = """[{"SourceFile":"probe","GPSLatitude":"0","Make":"deferred-fake"}]""";

    private readonly List<WrittenBlock> _blocks = [];
    private readonly object _sync = new();

    public event Action<string>? StdoutLine;

    public event Action? ProcessExited;

    public bool IsRunning { get; private set; }

    public string? LastStderrLine { get; set; }

    public string VersionText { get; set; } = "13.20";

    /// <summary>
    /// 已写入的块（含已放行的），按写入顺序。用例据此定位「第几个块对应哪个 marker」，
    /// 而不是靠固定等待赌时序。
    /// </summary>
    public IReadOnlyList<WrittenBlock> Blocks
    {
        get
        {
            lock (_sync)
            {
                return _blocks.ToArray();
            }
        }
    }

    /// <summary>按回显 marker 找块下标；-1 = 该块还没写进来。</summary>
    public int IndexOfMarker(string marker)
    {
        lock (_sync)
        {
            for (var i = 0; i < _blocks.Count; i++)
            {
                if (string.Equals(_blocks[i].Marker, marker, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    public Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var wasRunning = IsRunning;
        IsRunning = false;
        if (wasRunning)
        {
            ProcessExited?.Invoke();
        }

        return Task.CompletedTask;
    }

    public Task WriteStdinAsync(string text, CancellationToken cancellationToken)
    {
        var lines = text
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        var marker = ExtractEchoText(lines) ?? string.Empty;
        var isJsonBlock = lines.Any(l => string.Equals(l.Trim(), "-json", StringComparison.Ordinal));
        var isVersionBlock = lines.Any(l => string.Equals(l.Trim(), "-ver", StringComparison.Ordinal));

        // 健康 / 版本探针不含文件参数：同步整块产出，用例不控制它们（否则启动与健康检查挂死在自己手上）。
        if (!HasFileInput(lines))
        {
            EmitStdout(marker);
            if (isJsonBlock)
            {
                EmitStdout(ProbeJson);
            }

            if (isVersionBlock)
            {
                EmitStdout(VersionText);
            }

            EmitStdout("{ready}");
            return Task.CompletedTask;
        }

        lock (_sync)
        {
            _blocks.Add(new WrittenBlock(marker, isJsonBlock));
        }

        return Task.CompletedTask;
    }

    /// <summary>产出第 index 块的 <c>-echo1</c> 回显（该块开始处理的锚点）。</summary>
    public void EmitEcho(int index) => Emit(index, BlockPart.Echo);

    /// <summary>产出第 index 块的数据行（只有 <c>-json</c> 块有；擦除块无数据行）。</summary>
    public void EmitBody(int index) => Emit(index, BlockPart.Body);

    /// <summary>产出第 index 块的块结束信号 <c>{ready}</c>。</summary>
    public void EmitReady(int index) => Emit(index, BlockPart.Ready);

    private void Emit(int index, BlockPart part)
    {
        WrittenBlock block;
        lock (_sync)
        {
            block = _blocks[index];
        }

        switch (part)
        {
            case BlockPart.Echo:
                EmitStdout(block.Marker);
                break;

            case BlockPart.Body:
                if (block.IsJsonBlock)
                {
                    EmitStdout(ProbeJson);
                }

                break;

            case BlockPart.Ready:
                EmitStdout("{ready}");
                break;
        }
    }

    private void EmitStdout(string line) => StdoutLine?.Invoke(line);

    /// <summary>取命令块里 <c>-echo1</c> 的回显文本——ExifTool 真实语义：其后的文本原样回显。</summary>
    private static string? ExtractEchoText(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("-echo1", StringComparison.Ordinal))
            {
                continue;
            }

            var suffix = line["-echo1".Length..].Trim();
            if (suffix.Length > 0)
            {
                return suffix;
            }

            if (i + 1 >= lines.Count)
            {
                continue;
            }

            var next = lines[i + 1].Trim();
            if (next.Length > 0 && !next.StartsWith("-", StringComparison.Ordinal))
            {
                return next;
            }
        }

        return null;
    }

    /// <summary>
    /// 块里是否含「待解析文件」参数——探测 / 擦除块有，健康 / 版本探针没有。
    /// 判定与 <see cref="ProtocolLevelFakeExifToolProcess"/> 同口径（非选项 token，且不是 -echo1 的回显目标）。
    /// </summary>
    private static bool HasFileInput(IReadOnlyList<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("-echo1", StringComparison.Ordinal))
            {
                // -echo1 无内联后缀时，下一行是回显文本（不是文件参数）。
                if (line["-echo1".Length..].Trim().Length == 0)
                {
                    i++;
                }

                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private enum BlockPart
    {
        Echo,
        Body,
        Ready
    }

    /// <summary>一个已写入的命令块：<c>-echo1</c> 回显文本，以及它是否是 <c>-json</c> 探测块。</summary>
    internal sealed record WrittenBlock(string Marker, bool IsJsonBlock);
}
