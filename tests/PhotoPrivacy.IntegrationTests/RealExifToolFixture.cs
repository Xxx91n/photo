using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 真 ExifTool 用例的公共夹具：一张 1x1 的最小 JPEG，加上一次性的 exiftool 子进程调用。
/// 用例在临时目录里自己造带元数据的文件，不再指望某台机器上提前放好的那张图。
/// </summary>
internal static class RealExifToolFixture
{
    /// <summary>写进临时 JPEG 的 Make 标记，用来证明元数据真的落盘了。</summary>
    public const string TagMarker = "PhotoPrivacy19Probe";

    /// <summary>经典 1x1 最小 JPEG（含 JFIF 头），只当写入 EXIF 的载体，像素内容无意义。</summary>
    private const string MinimalJpegBase64 =
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwcJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPDs0NDT/" +
        "wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q==";

    public static string CreateWorkDirectory(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// 造一张带 EXIF 的临时 JPEG：先落最小图，再用一次 exiftool 写入 Make / Model / 拍摄时间，
    /// 写完马上读回 Make 确认真的落盘了（只看出退码不够，标签名写错时 exiftool 照样退 0）。
    /// </summary>
    public static async Task<string> CreateTaggedJpegAsync(string exifToolPath, string workDir)
    {
        var target = Path.Combine(workDir, "tagged.jpg");
        await File.WriteAllBytesAsync(target, Convert.FromBase64String(MinimalJpegBase64));

        var write = await RunOneShotAsync(exifToolPath, new[]
        {
            "-overwrite_original", "-q", "-q",
            $"-EXIF:Make={TagMarker}",
            "-EXIF:Model=RealExifToolProbeFixture",
            "-EXIF:DateTimeOriginal=2026:01:01 00:00:00",
            target
        });

        if (write.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"给临时 JPEG 写 EXIF 失败（exit={write.ExitCode}）：{Truncate(write.Stderr)}");
        }

        var landed = await ReadMakeAsync(exifToolPath, target);
        if (!string.Equals(landed, TagMarker, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"临时 JPEG 的 EXIF:Make 没写进去（读到 '{landed}'），后面擦除判定就失去意义。");
        }

        return target;
    }

    /// <summary>读回文件里的 EXIF:Make；擦除后应当为空串。</summary>
    public static async Task<string> ReadMakeAsync(string exifToolPath, string targetPath)
    {
        var read = await RunOneShotAsync(exifToolPath, new[] { "-s3", "-EXIF:Make", targetPath });
        if (read.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"读取 EXIF:Make 失败（exit={read.ExitCode}）：{Truncate(read.Stderr)}");
        }

        return read.Stdout.Trim();
    }

    /// <summary>
    /// 执行一次 exiftool：stdout / stderr 都读到结束（不读 stderr，管道写满会卡死）。
    /// 30 秒没退出就杀掉整棵进程树（进程卫生检查规则），再把退出码交回调用方。
    /// </summary>
    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunOneShotAsync(
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
            try { process.Kill(entireProcessTree: true); } catch { /* 已经退了 */ }
            // 杀进程会把管道关掉；顺手收掉读任务，别留下没人观察的取消异常。
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* 读任务被取消 */ }
            throw new TimeoutException($"exiftool one-shot 超过 30 秒没退出：{string.Join(" ", args)}");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return (process.ExitCode, stdout, stderr);
    }

    /// <summary>删临时目录，删不掉就算了（残留进临时目录，不影响用例判定）。</summary>
    public static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    private static string Truncate(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }
}
