using System.Diagnostics;
using System.Threading.Tasks;

namespace PhotoPrivacy.IntegrationTests.Smoke;

[Trait("Category", "Smoke")]
public sealed class ReleaseReadinessScriptValidationTests
{
    [Fact]
    public async Task ReleaseReadiness_Should_Fail_Fast_For_Runtime_All()
    {
        var repoRoot = FindRepoRoot();
        var psi = new ProcessStartInfo(
            "powershell",
            "-NoProfile -ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime all")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await WaitForExitWithTimeoutAsync(process, TimeSpan.FromSeconds(30), "release-readiness.ps1");
        var merged = await stdoutTask + Environment.NewLine + await stderrTask;

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Runtime 'all' is not valid", merged, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishApp_Should_Produce_Release_Entrypoints()
    {
        var repoRoot = FindRepoRoot();

        // 子进程里会再拉起长驻服务器（如 Roslyn 编译服务器），它们继承重定向管道，
        // 任何等 EOF 的读法都会挂死（ReadToEndAsync / WaitForExitAsync 都等）。改为：
        // 经临时 .cmd 把输出直接落到文件，进程本身不挂管道，只等退出再读文件。
        var outFile = Path.Combine(Path.GetTempPath(), "pp-publish-" + Guid.NewGuid().ToString("N") + ".out.log");
        var errFile = Path.Combine(Path.GetTempPath(), "pp-publish-" + Guid.NewGuid().ToString("N") + ".err.log");
        var cmdFile = Path.Combine(Path.GetTempPath(), "pp-publish-" + Guid.NewGuid().ToString("N") + ".cmd");
        File.WriteAllText(cmdFile,
            "@echo off" + Environment.NewLine +
            "chcp 65001 > nul" + Environment.NewLine +
            "powershell -ExecutionPolicy Bypass -File scripts\\publish-app.ps1 -Version 0.1.0-preview -Runtime win-x64 -Framework net10.0 -SelfContained true -Zip false 1> \"" + outFile + "\" 2> \"" + errFile + "\"" + Environment.NewLine +
            "exit /b %ERRORLEVEL%" + Environment.NewLine);

        var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmdFile + "\"")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false
        };

        var merged = string.Empty;
        int exitCode;
        try
        {
            using var process = Process.Start(psi)!;
            await WaitForExitWithTimeoutAsync(process, ReadPublishTimeout(), "publish-app.ps1");
            exitCode = process.ExitCode;
            var stdout = await ReadSharedAsync(outFile);
            var stderr = await ReadSharedAsync(errFile);
            merged = stdout + Environment.NewLine + stderr;
        }
        finally
        {
            File.Delete(cmdFile);
            TryDeleteFile(outFile);
            TryDeleteFile(errFile);
        }

        Assert.True(exitCode == 0, $"publish-app.ps1 failed (exit {exitCode})\n{merged}");

        // 旧 Avalonia UI 退役后（#61）托盘资产不再单独落盘，改为核验现役发布契约：
        // 与 release.yml 的 Verify structure 同一组入口文件。
        var releaseRoot = Path.Combine(repoRoot, "release", "win-x64");
        foreach (var relative in new[]
                 {
                     "PhotoPrivacy.exe",
                     "PhotoPrivacyWorker.exe",
                     Path.Combine("config", "config.sample.json"),
                 })
        {
            var path = Path.Combine(releaseRoot, relative);
            Assert.True(File.Exists(path), $"missing published entry: {path}\n{merged}");
        }
    }

    [Fact]
    public void ReadPublishTimeout_Should_Default_To_900s_When_Env_Unset()
    {
        var original = Environment.GetEnvironmentVariable(PublishTimeoutEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(PublishTimeoutEnvVar, null);
            Assert.Equal(TimeSpan.FromSeconds(900), ReadPublishTimeout());
        }
        finally
        {
            Environment.SetEnvironmentVariable(PublishTimeoutEnvVar, original);
        }
    }

    [Fact]
    public void ReadPublishTimeout_Should_Honor_Positive_Env_Override()
    {
        var original = Environment.GetEnvironmentVariable(PublishTimeoutEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(PublishTimeoutEnvVar, "600");
            Assert.Equal(TimeSpan.FromSeconds(600), ReadPublishTimeout());
        }
        finally
        {
            Environment.SetEnvironmentVariable(PublishTimeoutEnvVar, original);
        }
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void ReadPublishTimeout_Should_Fall_Back_To_900s_On_Invalid_Env(string raw)
    {
        var original = Environment.GetEnvironmentVariable(PublishTimeoutEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(PublishTimeoutEnvVar, raw);
            Assert.Equal(TimeSpan.FromSeconds(900), ReadPublishTimeout());
        }
        finally
        {
            Environment.SetEnvironmentVariable(PublishTimeoutEnvVar, original);
        }
    }

    // 参数读点：publish 冒烟预算默认 900s，环境变量 PHOTOPRIVACY_PUBLISH_TIMEOUT_SECONDS 可覆盖。
    // 根因：本机 Release 自包含发布实测 5–6 分钟，300s 硬编码在多窗口并行构建时必超时。
    internal const string PublishTimeoutEnvVar = "PHOTOPRIVACY_PUBLISH_TIMEOUT_SECONDS";

    internal static TimeSpan ReadPublishTimeout()
    {
        const int defaultSeconds = 900;
        var raw = Environment.GetEnvironmentVariable(PublishTimeoutEnvVar);
        return int.TryParse(raw, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(defaultSeconds);
    }

    // 子进程拉起的常驻服务器可能还攥着输出文件句柄；按共享方式读，别让读取本身失败。
    private static async Task<string> ReadSharedAsync(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static async Task WaitForExitWithTimeoutAsync(Process process, TimeSpan timeout, string scriptName)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(scriptName + " hung (timeout " + timeout.TotalSeconds + "s)");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PhotoPrivacy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root from test runtime directory.");
    }
}
