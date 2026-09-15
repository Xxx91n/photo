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

    public string VersionText { get; set; } = "13.20";

    public Task StartAsync(string exePath, string[] args, CancellationToken cancellationToken)
    {
        StartCalls++;
        ExePath = exePath;
        _args.Clear();
        _args.AddRange(args);
        IsRunning = true;
        return Task.CompletedTask;
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
        var delayMs = isVersionBlock ? VersionResponseDelayMs : (isJsonBlock ? 0 : HealthResponseDelayMs);

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

    private void EmitStdout(string line) => StdoutLine?.Invoke(line);
}
