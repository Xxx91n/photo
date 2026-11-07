using System.Diagnostics;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// #39 实测：stay_open 协议「块按写入顺序整块处理、回显与 <c>{ready}</c> 一一对应」假设，
/// 在真实 ExifTool 进程上以多并发写验证（CI job <c>exiftool-stayopen</c>：ubuntu + windows
/// 双腿、钉扎 13.61 真跑；产品主平台是 Windows 管道，双腿都要过）。
///
/// 桥的全部完成判定（探测 JSON 收集窗口、擦除写盘、<c>CompleteBlockJustEchoed</c> 的归属、
/// <see cref="ExifToolBridge.WriteBlockAsync"/> 的块级串行化）都押在这条假设上。本用例覆盖的
/// 失败形态：
/// ① 并发块的行互相插入 → 探测 JSON 被别块 marker/输出污染 → 有元数据文件被判
///    Skipped_NoMetadata / Cleaned_NoOp 静默跳过（#111/#39 登记的静默失败形态）；
/// ② 外块 <c>{ready}</c> 提前结清本块 → Cleaned_Modified 报早 → 每轮后用一次性 exiftool
///    独立复核 EXIF 确实已清（不复用桥自己的结论）；
/// ③ 大输出窗口：模板带 ~120 KB XMP，把探测 JSON 输出推过管道缓冲量级——长窗口下窗口
///    归属仍不得被并发块污染（写侧整块性由 <c>_stdinGate</c> 闸内「写+flush」保证，本用例
///    验的是它下游的整块处理与归属）。
///
/// 边界与残余（诚实口径，Design(#39) 注释引用此处）：
/// - 证伪力 = 真进程并发路径未撞红：ExifTool stay_open 是单线程读循环，黑盒用例没有交错
///   观测手段；抓得住一切跨块插入/错配的下游表现，抓不住 exiftool 内部结构性的处理顺序。
/// - 产品同桥并发写还有 IPC 状态查询驱动的版本探针：版本只解析一次（EnsureVersionAsync 对
///   已知版本直接短路），该形态无法黑盒重发，属残余。
/// - 健康探针刻意不混入擦除队列中段：500 ms 预算排在深擦除队列后面会超时触发重启，那是
///   与被测假设无关的重启风暴（#82 裁定健康检查副作用只属于文件路径）；本用例里健康探针
///   只出现在队列空窗（起跑处）。
/// - 本机无 ExifTool 时早退（RealExifToolProbeTest 先例）；CI 由 job 的 ver 门禁保证这条
///   早退不会发生在门禁里。夹具生成失败必须红（Assert.Fail）——#82：不得静默 skip。
///
/// 用例内的一次性 exiftool spawn 只属于夹具生成/独立复核，不是产品路径的每文件 spawn。
/// </summary>
[Trait("Category", "ExifTool")]
public sealed class StayOpenConcurrentBlockTests : IntegrationTestBase
{
    /// <summary>经典 1x1 最小 JPEG（含 JFIF 头），仅作为 EXIF 写入载体，像素内容无意义。</summary>
    private const string MinimalJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwcJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDs0NDT/" +
        "wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==";

    private const string TagMarker = "StayOpen39Probe";

    /// <summary>并发度取 4：多并发写成立，且只在起跑空窗出现的健康探针不会被压出 500 ms 预算。</summary>
    private const int FilesPerRound = 4;

    private const int Rounds = 10;

    /// <summary>把探测 JSON 输出窗口推过 64 KB 管道缓冲量级的 XMP 填充长度。</summary>
    private const int OversizePayloadLength = 120 * 1024;

    [Fact]
    public async Task Concurrent_Wipe_Blocks_On_Single_Bridge_Should_Not_Cross_Pollute()
    {
        var fixture = await TryStartFixtureAsync();
        if (fixture is null)
        {
            return;
        }

        try
        {
            Console.WriteLine($"ExifTool version: {fixture.Bridge.VersionText}");

            for (var round = 1; round <= Rounds; round++)
            {
                var files = fixture.MaterializeFiles(round, FilesPerRound);

                // 并发提交：4 个任务各自「探测块 → 擦除块」交错进同一 stay_open stdin，
                // 每块必须被整块处理、每块的 {ready} 只结清自己（①类失败形态在此落红）。
                var results = await Task.WhenAll(
                    files.Select(f => fixture.Bridge.WipeMetadataAsync(f, fixture.Token)));

                Assert.All(results, (result, i) => Assert.True(
                    result == WipeResult.Cleaned_Modified,
                    $"#39 实测撞红：第 {round} 轮 {Path.GetFileName(files[i])} -> {result}"
                    + "（有元数据文件未被 Cleaned_Modified，疑并发块跨块污染/错配）"));

                // ②外块 {ready} 提前结清会让 Cleaned_Modified 报早——不复用桥的结论，
                // 每轮后用一次性 exiftool 独立复核 EXIF 确实已清。
                foreach (var file in files)
                {
                    await fixture.AssertExifClearedAsync(file);
                }
            }
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static AppConfig BuildConfig(string exifToolPath)
        => AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                Path = exifToolPath
            }
        };

    /// <summary>
    /// 定位并验证真 ExifTool：一次 spawn <c>-ver</c> 探活。文件不存在 → 返回 null（环境未提供，
    /// 早退口径同 RealExifToolProbeTest）；文件存在但探活失败 → Assert.Fail（环境损坏，
    /// 静默早退违背 #82「不得静默 skip」）。CI 门禁由 exiftool-stayopen job 的 ver 步先行
    /// 验证可用性，两种情形都不会发生在门禁里。
    /// </summary>
    private static async Task<string?> TryLocateRealExifToolAsync()
    {
        var candidate = ExifToolLocator.TryResolve();
        if (!File.Exists(candidate))
        {
            return null;
        }

        var probe = await RunExifToolOneShotAsync(candidate, new[] { "-ver" });
        if (probe.ExitCode != 0 || string.IsNullOrWhiteSpace(probe.Stdout))
        {
            Assert.Fail($"#39 ExifTool 探活失败（exit={probe.ExitCode}）：{Truncate(probe.Stderr)}");
        }

        return candidate;
    }

    /// <summary>
    /// 起夹具： tagged 模板（写后独立回读校验）+ 已启动的桥。失败即红——夹具生成失败是
    /// 真问题，静默早退会让整个实测假绿（#82：不得静默 skip）。仅「本机无 ExifTool」
    /// 一种情形返回 null（CI 的 ver 门禁保证门禁里不会走到）。
    /// </summary>
    private static async Task<StayOpenFixture?> TryStartFixtureAsync()
    {
        var exe = await TryLocateRealExifToolAsync();
        if (exe is null)
        {
            Console.WriteLine("SKIP: 本机无可用 ExifTool（CI exiftool-stayopen job 提供门禁环境）。");
            return null;
        }

        var workDir = Path.Combine(Path.GetTempPath(), "stayopen39-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var template = Path.Combine(workDir, "template.jpg");
        await File.WriteAllBytesAsync(template, Convert.FromBase64String(MinimalJpegBase64));

        // 超大 tag 值经 -TAG<=文件 传入：Windows CreateProcess 命令行上限 32K，
        // 直接把 120KB 值放进参数会在起进程时就炸（Win32Exception）。
        var payloadPath = Path.Combine(workDir, "oversize.txt");
        await File.WriteAllTextAsync(payloadPath, new string('x', OversizePayloadLength));

        var write = await RunExifToolOneShotAsync(exe, new[]
        {
            "-overwrite_original", "-q", "-q",
            $"-EXIF:Make={TagMarker}",
            "-EXIF:Model=ConcurrencyMatrix",
            "-EXIF:GPSLatitude=37.5",
            "-EXIF:GPSLongitude=-122.1",
            "-EXIF:DateTimeOriginal=2026:01:01 00:00:00",
            $"-XMP:Description<={payloadPath}",
            template
        });
        if (write.ExitCode != 0)
        {
            TryDelete(workDir);
            Assert.Fail($"#39 夹具生成失败（exiftool 写 EXIF exit={write.ExitCode}）：{Truncate(write.Stderr)}");
        }

        var check = await RunExifToolOneShotAsync(exe, new[] { "-s3", "-EXIF:Make", template });
        if (check.ExitCode != 0 || !string.Equals(check.Stdout.Trim(), TagMarker, StringComparison.Ordinal))
        {
            TryDelete(workDir);
            Assert.Fail(
                $"#39 夹具回读校验失败（exit={check.ExitCode}, stdout={Truncate(check.Stdout)}）"
                + "：无法证实模板带 EXIF，后续判定失去基准。");
        }

        var bridge = new ExifToolBridge(new ProcessExifToolProcess(), BuildConfig(exe));
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await bridge.StartAsync(cts.Token);
        return new StayOpenFixture(exe, bridge, cts, template, workDir);
    }

    private static string Truncate(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 夹具清理 best-effort：残留进临时目录，不影响判定。
        }
    }

    /// <summary>一次性执行 exiftool：双流都读完（stderr 不读会在管道填满时死锁）、
    /// 30 s 超时后 tree-kill（进程卫生守卫口径），再取 ExitCode。</summary>
    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunExifToolOneShotAsync(
        string exe, IReadOnlyList<string> args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException($"exiftool one-shot 超过 30s 未退出：{string.Join(" ", args)}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    /// <summary>一次实测运行的夹具与桥聚合：模板、工作目录、并发预算与独立复核都从这里走。</summary>
    private sealed class StayOpenFixture
    {
        private readonly string _exe;
        private readonly CancellationTokenSource _cts;
        private readonly string _workDir;

        public StayOpenFixture(string exe, ExifToolBridge bridge, CancellationTokenSource cts, string template, string workDir)
        {
            _exe = exe;
            Bridge = bridge;
            _cts = cts;
            Template = template;
            _workDir = workDir;
        }

        public ExifToolBridge Bridge { get; }

        public CancellationToken Token => _cts.Token;

        public string Template { get; }

        public List<string> MaterializeFiles(int round, int count)
        {
            var files = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var target = Path.Combine(_workDir, $"r{round}-f{i}.jpg");
                File.Copy(Template, target, overwrite: true);
                files.Add(target);
            }

            return files;
        }

        /// <summary>绕开桥，用一次性 exiftool 独立复核 EXIF:Make 已清——外块 {ready} 提前
        /// 结清时桥会报 Cleaned_Modified，只有这个复核能抓住「报早」。</summary>
        public async Task AssertExifClearedAsync(string file)
        {
            var check = await RunExifToolOneShotAsync(_exe, new[] { "-s3", "-EXIF:Make", file });
            Assert.True(
                check.ExitCode == 0 && string.IsNullOrWhiteSpace(check.Stdout),
                $"#39 实测撞红：{Path.GetFileName(file)} 报 Cleaned_Modified 但 EXIF:Make 仍在"
                + $"（疑外块 {{ready}} 提前结清本块）：exit={check.ExitCode}, stdout={check.Stdout.Trim()}");
        }

        public async Task DisposeAsync()
        {
            try
            {
                await Bridge.StopAsync(CancellationToken.None);
            }
            finally
            {
                Bridge.Dispose();
                _cts.Dispose();
                TryDelete(_workDir);
            }
        }
    }
}
