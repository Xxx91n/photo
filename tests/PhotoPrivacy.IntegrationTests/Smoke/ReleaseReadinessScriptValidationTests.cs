using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class ReleaseReadinessScriptValidationTests
{
    [Fact]
    public async Task ReleaseReadiness_Should_Fail_Fast_For_Runtime_All()
    {
        var repoRoot = FindRepoRoot();
        var psi = new ProcessStartInfo(
            "powershell",
            "-ExecutionPolicy Bypass -File scripts/release-readiness.ps1 -Version 0.1.0-preview -Runtime all")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var merged = stdout + Environment.NewLine + stderr;

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
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var merged = stdout + Environment.NewLine + stderr;

        Assert.Equal(0, process.ExitCode);

        var assetPath = Path.Combine(repoRoot, "publish", "app", "0.1.0-preview", "win-x64", "Assets", "tray-dot-16.png.base64");
        Assert.True(File.Exists(assetPath), $"missing tray asset: {assetPath}\n{merged}");
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
