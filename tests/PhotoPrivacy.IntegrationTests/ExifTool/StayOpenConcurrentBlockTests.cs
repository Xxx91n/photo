using System.Diagnostics;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// #39 实测：stay_open 协议「块按写入顺序整块处理、回显与 <c>{ready}</c> 一一对应」假设，
/// 在真实 ExifTool 进程上以多并发写验证。桥的全部完成判定（探测 JSON 收集、擦除写盘、
/// <see cref="ExifToolBridge.WriteBlockAsync"/> 的块级串行化）都押在这条假设上——
/// 假设破了的表现即 #111/#39 登记的静默失败形态：
/// ① 并发块的行互相插入 → 探测 JSON 被别块 marker/输出污染 → 解析失败 → 有元数据的文件被判
///    Skipped_NoMetadata 静默跳过；
/// ② 外块 <c>{ready}</c> 提前结清本块 → 报 Cleaned_Modified 但 EXIF 实际未清（用独立复核钉住）。
///
/// 并发形状与产品口径对齐：单桥多任务并发提交命令块（<c>_stdinGate</c> 只串行化「写」，
/// 多并发写正是该闸存在的理由）；每轮以 Task.WhenAll 为屏障，健康探针只出现在队列空窗
/// （轮首，互相只排在对方健康块后面），不把 500 ms 健康预算压爆而触发与被测假设无关的重启。
///
/// 注意：`Category=ExifTool` 被 ci.yml 与 release.yml 的默认过滤排除；由 ci.yml 的
/// exiftool-stayopen job 下载钉扎版 ExifTool 真跑本类。装有 ExifTool 的机器可显式跑：
/// <c>dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"</c>。
/// ExifTool 不可用（未装/不可执行）或夹具生成失败时按 RealExifToolProbeTest 先例静默早退。
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

    /// <summary>并发度取 4：与「多并发写」同存，又不把队列压到健康探针 500 ms 预算之外（见类注释）。</summary>
    private const int FilesPerRound = 4;

    private const int Rounds = 10;

    [Fact]
    public async Task Concurrent_Wipe_Blocks_On_Single_Bridge_Should_Not_Cross_Pollute()
    {
        var exe = TryLocateRealExifTool();
        if (exe is null)
        {
            Console.WriteLine("SKIP: 本机无可用 ExifTool（CI 由 exiftool-stayopen job 提供）。");
            return;
        }

        var workDir = CreateWorkDir();
        var bridge = new ExifToolBridge(new ProcessExifToolProcess(), BuildConfig(exe));
        try
        {
            var template = await TryCreateTaggedTemplateAsync(exe, workDir);
            if (template is null)
            {
                Console.WriteLine("SKIP: 夹具生成失败（无法向 JPEG 写入并回读 EXIF），环境问题不判产品红。");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await bridge.StartAsync(cts.Token);
            Console.WriteLine($"ExifTool version: {bridge.VersionText}");

            for (var round = 1; round <= Rounds; round++)
            {
                var files = MaterializeFiles(template, workDir, round, FilesPerRound);

                // 并发提交：4 个任务各自「探测块 → 擦除块」交错进同一 stay_open stdin，
                // 每块必须被整块处理、每块的 {ready} 只结清自己——任何跨块污染都会把
                // 有元数据的文件打成 Skipped_NoMetadata / Cleaned_NoOp（①类失败形态）。
                var results = await Task.WhenAll(
                    files.Select(f => bridge.WipeMetadataAsync(f, cts.Token)));

                var unexpected = results
                    .Select((result, i) => (result, file: files[i]))
                    .Where(x => x.result != WipeResult.Cleaned_Modified)
                    .ToList();

                Assert.True(
                    unexpected.Count == 0,
                    "#39 实测撞红：并发块写入出现跨块污染/错配（有元数据文件未被判 Cleaned_Modified）：\n"
                    + string.Join("\n", unexpected.Select(x => $"{Path.GetFileName(x.file)} -> {x.result}")));
            }
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
            TryDelete(workDir);
        }
    }

    [Fact]
    public async Task Concurrent_Wipe_Blocks_Should_Really_Clear_Exif()
    {
        var exe = TryLocateRealExifTool();
        if (exe is null)
        {
            Console.WriteLine("SKIP: 本机无可用 ExifTool（CI 由 exiftool-stayopen job 提供）。");
            return;
        }

        var workDir = CreateWorkDir();
        var bridge = new ExifToolBridge(new ProcessExifToolProcess(), BuildConfig(exe));
        try
        {
            var template = await TryCreateTaggedTemplateAsync(exe, workDir);
            if (template is null)
            {
                Console.WriteLine("SKIP: 夹具生成失败（无法向 JPEG 写入并回读 EXIF），环境问题不判产品红。");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await bridge.StartAsync(cts.Token);

            const int fileCount = 8;
            var files = MaterializeFiles(template, workDir, round: 1, fileCount);
            var results = await Task.WhenAll(
                files.Select(f => bridge.WipeMetadataAsync(f, cts.Token)));

            // 先把协议层结论如实报出来（结果异常时断言消息带上它，别只留下独立复核的干瘪红）。
            Assert.True(
                results.All(r => r == WipeResult.Cleaned_Modified),
                "#39 实测撞红：并发块写入出现跨块污染/错配：\n"
                + string.Join("\n", files.Zip(results).Select(x => $"{Path.GetFileName(x.First)} -> {x.Second}")));

            // ②类失败形态：外块 {ready} 提前结清本块会让 Cleaned_Modified 报早，
            // 擦除实际尚未落盘。这里绕开桥，用一次性 exiftool 独立复核 EXIF 确实被清。
            foreach (var file in files)
            {
                var check = RunExifToolOneShot(exe, new[] { "-s3", "-EXIF:Make", file });
                Assert.True(
                    check.ExitCode == 0 && string.IsNullOrWhiteSpace(check.Stdout),
                    $"#39 实测撞红：{Path.GetFileName(file)} 报 Cleaned_Modified 但 EXIF:Make 仍在"
                    + "（疑为外块 {ready} 提前结清本块）："
                    + $"exit={check.ExitCode}, stdout={check.Stdout.Trim()}");
            }
        }
        finally
        {
            await bridge.StopAsync(CancellationToken.None);
            TryDelete(workDir);
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

    private static string CreateWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stayopen39-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static List<string> MaterializeFiles(string template, string workDir, int round, int count)
    {
        var files = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var target = Path.Combine(workDir, $"r{round}-f{i}.jpg");
            File.Copy(template, target, overwrite: true);
            files.Add(target);
        }

        return files;
    }

    private static string? TryLocateRealExifTool()
    {
        var candidate = ExifToolLocator.TryResolve();
        if (!File.Exists(candidate))
        {
            return null;
        }

        var probe = RunExifToolOneShot(candidate, new[] { "-ver" });
        return probe.ExitCode == 0 && !string.IsNullOrWhiteSpace(probe.Stdout) ? candidate : null;
    }

    /// <summary>
    /// 生成带富 EXIF 的模板 JPEG：先写入再独立回读校验，任一步失败返回 null（调用方按环境问题早退）。
    /// </summary>
    private static async Task<string?> TryCreateTaggedTemplateAsync(string exe, string workDir)
    {
        var template = Path.Combine(workDir, "template.jpg");
        await File.WriteAllBytesAsync(template, Convert.FromBase64String(MinimalJpegBase64));

        var write = RunExifToolOneShot(exe, new[]
        {
            "-overwrite_original", "-q", "-q",
            $"-EXIF:Make={TagMarker}",
            "-EXIF:Model=ConcurrencyMatrix",
            "-EXIF:GPSLatitude=37.5",
            "-EXIF:GPSLongitude=-122.1",
            "-EXIF:DateTimeOriginal=2026:01:01 00:00:00",
            "-XMP:Description=stayopen39 concurrent block matrix",
            template
        });
        if (write.ExitCode != 0)
        {
            Console.WriteLine($"fixture write failed: exit={write.ExitCode}");
            return null;
        }

        var check = RunExifToolOneShot(exe, new[] { "-s3", "-EXIF:Make", template });
        return check.ExitCode == 0 && string.Equals(check.Stdout.Trim(), TagMarker, StringComparison.Ordinal)
            ? template
            : null;
    }

    private static (int ExitCode, string Stdout) RunExifToolOneShot(string exe, IReadOnlyList<string> args)
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
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(TimeSpan.FromSeconds(30));
        return (process.ExitCode, stdout);
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
}
