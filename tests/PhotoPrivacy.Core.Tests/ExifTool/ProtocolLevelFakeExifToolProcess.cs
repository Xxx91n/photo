using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.Core.Tests.ExifTool;

/// <summary>
/// 票 01（A-001 第 5 条）：协议级 ExifTool fake——按 stay_open 协议语义执行命令块。
///
/// 旧 fake 是同义反复：它从 SUT 写出的命令文本里搜出“SUT 正在等待的 marker”再回显，
/// 于是“桥等什么”与“fake 发什么”永远一致，A-001 的挂死链永远测不出来。本 fake 的三条差异：
/// 1) 输出只由参数语义决定——`-echo1 X` 回显 X（ExifTool 真实语义）、含 `-json` 的块产出探测 JSON、
///    含 `-ver` 的块产出版本号、`-execute` 收尾产 `{ready}`；没有任何 marker 名字特判，
///    也没有“是否发射 SUT 期待的那个 marker”的开关。
/// 2) 故障注入是进程级的（<see cref="SilenceOutput"/> = 进程无响应），不是“抑制 SUT 期待的那一行”。
/// 3) 因此命令块里没有 TASK_DONE_ 时，fake 就不会产 TASK_DONE_——等待它的桥会真挂死，
///    行为测试才能抓住 A-001（见 UnsupportedFormatPipelineTests）。
/// </summary>
internal sealed class ProtocolLevelFakeExifToolProcess : IExifToolProcess
{
    private readonly List<string> _args = [];
    private readonly List<string> _writes = [];
    private int _activeStartCalls;

    public event Action<string>? StdoutLine;

    public string ExePath { get; private set; } = string.Empty;

    public IReadOnlyList<string> Args => _args;

    public IReadOnlyList<string> Writes => _writes;

    public bool IsRunning { get; private set; }

    public string? LastStderrLine { get; set; }

    public int StartCalls { get; private set; }

    public int StopCalls { get; private set; }

    /// <summary>故障注入：进程级静默（模拟 ExifTool 卡死/无响应），不产出任何 stdout。</summary>
    public bool SilenceOutput { get; set; }

    /// <summary>无 -json/-ver 的块（健康检查、擦除）的回包延迟，单位毫秒。</summary>
    public int HealthResponseDelayMs { get; set; }

    /// <summary>含 -ver 的版本探测块的回包延迟，单位毫秒。</summary>
    public int VersionResponseDelayMs { get; set; }

    /// <summary>
    /// 票 05（A-005）成本注入：命令块里出现「文件参数」（非选项 token，且非 -echo1 的回显目标）时的回包延迟，单位毫秒。
    /// 模拟 ExifTool 真去解析一个输入文件的开销——用于复现「把 exiftool 自身当照片读 → 慢机每文件重启进程」。
    /// 为 0 时退回既有 HealthResponseDelayMs/VersionResponseDelayMs 选择逻辑（既有用例零影响）。
    /// </summary>
    public int FileInputParseDelayMs { get; set; }

    /// <summary>票 05（A-005）：StartAsync 耗时注入，单位毫秒——用于拉开并发启动窗口。</summary>
    public int StartDelayMs { get; set; }

    /// <summary>票 05（A-005）：StartAsync 并发进入峰值（>1 说明重启路径未持 _startLock）。</summary>
    public int MaxConcurrentStartCalls { get; private set; }

    public string VersionText { get; set; } = "13.20";

    public async Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _activeStartCalls);
        MaxConcurrentStartCalls = Math.Max(MaxConcurrentStartCalls, active);
        try
        {
            StartCalls++;
            ExePath = exePath;
            _args.Clear();
            _args.AddRange(args);
            IsRunning = true;

            if (StartDelayMs > 0)
            {
                await Task.Delay(StartDelayMs, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeStartCalls);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCalls++;
        IsRunning = false;
        return Task.CompletedTask;
    }

    public Task WriteStdinAsync(string text, CancellationToken cancellationToken)
    {
        _writes.Add(text);

        if (SilenceOutput)
        {
            return Task.CompletedTask;
        }

        var lines = text
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        var isVersionBlock = lines.Any(l => string.Equals(l.Trim(), "-ver", StringComparison.Ordinal));
        var isJsonBlock = lines.Any(l => string.Equals(l.Trim(), "-json", StringComparison.Ordinal));
        var delayMs = HasFileInput(lines) && FileInputParseDelayMs > 0
            ? FileInputParseDelayMs
            : (isVersionBlock ? VersionResponseDelayMs : (isJsonBlock ? 0 : HealthResponseDelayMs));

        void EmitBlock()
        {
            // ExifTool 真实语义：-echo1 把其后的文本原样回显到 stdout（先于文件处理输出）。
            foreach (var echo in ExtractEchoTexts(lines))
            {
                EmitStdout(echo);
            }

            if (isJsonBlock)
            {
                // -json 探测输出：带可清除字段的元数据数组。
                EmitStdout("""[{"SourceFile":"probe","GPSLatitude":"0","Make":"protocol-fake"}]""");
            }

            if (isVersionBlock)
            {
                EmitStdout(VersionText);
            }

            EmitStdout("{ready}");
        }

        if (delayMs <= 0)
        {
            EmitBlock();
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            EmitBlock();
        });

        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> ExtractEchoTexts(IReadOnlyList<string> lines)
    {
        var echoes = new List<string>();
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
                echoes.Add(suffix);
                continue;
            }

            if (i + 1 >= lines.Count)
            {
                continue;
            }

            var next = lines[i + 1].Trim();
            if (next.Length > 0 && !next.StartsWith("-", StringComparison.Ordinal))
            {
                echoes.Add(next);
            }
        }

        return echoes;
    }

    /// <summary>
    /// 票 05（A-005）：判定命令块里是否存在「文件参数」——既非选项（不以 - 开头）、
    /// 也不是 -echo1 回显目标的 token。健康探针把 exiftool 自身路径喂进来时即命中此处。
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

    private void EmitStdout(string line) => StdoutLine?.Invoke(line);
}
