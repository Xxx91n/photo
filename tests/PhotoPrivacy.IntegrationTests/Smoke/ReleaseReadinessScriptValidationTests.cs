using System.Diagnostics;
using System.Threading.Tasks;

namespace PhotoPrivacy.IntegrationTests.Smoke;

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
        var waitTask = process.WaitForExitAsync();
        var finished = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(30)));
        if (finished != waitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("release-readiness.ps1 hung (timeout 30s)");
        }

        await waitTask;
        var merged = await stdoutTask + Environment.NewLine + await stderrTask;

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("Runtime 'all' is not valid", merged, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_Should_Not_Reference_Legacy_Cli_Project()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        var readme = File.ReadAllText(readmePath);

        Assert.DoesNotContain("src/PhotoPrivacy.Cli", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PhotoPrivacy.Cli", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishApp_Should_Copy_Tray_Assets_To_Publish_Root()
    {
        var repoRoot = FindRepoRoot();
        foreach (var proc in Process.GetProcessesByName("PhotoPrivacy"))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
            }
            catch
            {
                // best effort for test isolation
            }
        }

        foreach (var proc in Process.GetProcessesByName("PhotoPrivacyWorker"))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
            }
            catch
            {
                // best effort for test isolation
            }
        }

        var psi = new ProcessStartInfo(
            "powershell",
            "-ExecutionPolicy Bypass -File scripts/publish-app.ps1 -Version 0.1.0-preview -Runtime win-x64 -Framework net10.0 -SelfContained true -Zip false")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync();
        var publishTimeout = ReadPublishTimeout();
        var finished = await Task.WhenAny(waitTask, Task.Delay(publishTimeout));
        if (finished != waitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"publish-app.ps1 hung (timeout {publishTimeout.TotalSeconds}s)");
        }

        await waitTask;
        var merged = await stdoutTask + Environment.NewLine + await stderrTask;

        Assert.Equal(0, process.ExitCode);

        var assetPath = Path.Combine(repoRoot, "release", "win-x64", "Assets", "tray-dot-16.png.base64");
        Assert.True(File.Exists(assetPath), $"missing tray asset: {assetPath}\n{merged}");
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

    // 参数读点（backlog B01）：publish 冒烟预算默认 900s，环境变量 PHOTOPRIVACY_PUBLISH_TIMEOUT_SECONDS 可覆盖。
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
