using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class DryRunOutputFlowTests
{
    private static readonly TimeSpan CliRunTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task CliHost_Should_Respect_Config_DryRun_And_Fixed_Output()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-dryrun-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var clean = Path.Combine(root, "clean");
        var audit = Path.Combine(root, "audit");
        Directory.CreateDirectory(hot);
        Directory.CreateDirectory(clean);
        Directory.CreateDirectory(audit);

        try
        {
            var sourceFile = Path.Combine(hot, "a.jpg");
            File.WriteAllText(sourceFile, "dummy");

            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": {
                "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": true,
                "extra_exiftool_args": []
              },
              "watch": {
                "hot_folder": "{{EscapePath(hot)}}",
                "include_subdirectories": true,
                "debounce_ms": 100,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "fixed_directory",
                "output_directory": "{{EscapePath(clean)}}"
              },
              "retry": {
                "max_attempts": 1,
                "backoff_seconds": [0]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak",
                "retention": "keep"
              },
              "quarantine": {
                "enabled": true,
                "directory": "{{EscapePath(Path.Combine(root, "quarantine"))}}"
              },
              "audit": {
                "log_directory": "{{EscapePath(audit)}}",
                "retain_days": 7,
                "diagnostic_mode": true
              }
            }
            """);

            var result = await RunCliAsync(configPath, CliRunTimeout);
            Assert.Equal(0, result.ExitCode);

            var copied = Path.Combine(clean, "a.jpg");
            Assert.True(File.Exists(copied));

            var auditFile = Directory.GetFiles(audit, "audit-*.jsonl").Single();
            var content = File.ReadAllText(auditFile);
            Assert.Contains("exiftool_started", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dry_run_wipe_skipped", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("file_processing_succeeded", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CliHost_Should_Exit_When_ExifTool_Path_Is_Bad()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-livefail-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(root, "audit");
        Directory.CreateDirectory(hot);
        Directory.CreateDirectory(audit);

        try
        {
            var sourceFile = Path.Combine(hot, "a.jpg");
            File.WriteAllText(sourceFile, "dummy");

            var badExif = Path.Combine(root, "bad", "ExifTool.exe");
            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": {
                "path": "{{EscapePath(badExif)}}",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": false,
                "extra_exiftool_args": []
              },
              "watch": {
                "hot_folder": "{{EscapePath(hot)}}",
                "include_subdirectories": true,
                "debounce_ms": 100,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "fixed_directory",
                "output_directory": "{{EscapePath(Path.Combine(root, "clean"))}}"
              },
              "retry": {
                "max_attempts": 1,
                "backoff_seconds": [0]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak",
                "retention": "keep"
              },
              "quarantine": {
                "enabled": true,
                "directory": "{{EscapePath(Path.Combine(root, "quarantine"))}}"
              },
              "audit": {
                "log_directory": "{{EscapePath(audit)}}",
                "retain_days": 7,
                "diagnostic_mode": true
              }
            }
            """);

            var result = await RunCliAsync(configPath, CliRunTimeout);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("exiftool.path not found", result.Stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string EscapePath(string path)
    {
        return path.Replace("\\", "\\\\");
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

    [Fact]
    public async Task CliHost_Should_Print_Effective_Config_And_Exit_When_Requested()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-printcfg-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(root, "audit");
        Directory.CreateDirectory(hot);
        Directory.CreateDirectory(audit);

        try
        {
            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": {
                "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": true,
                "extra_exiftool_args": []
              },
              "watch": {
                "hot_folder": "{{EscapePath(hot)}}",
                "include_subdirectories": true,
                "debounce_ms": 100,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "fixed_directory",
                "output_directory": "{{EscapePath(Path.Combine(root, "clean"))}}"
              },
              "retry": {
                "max_attempts": 1,
                "backoff_seconds": [0]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak",
                "retention": "keep"
              },
              "quarantine": {
                "enabled": true,
                "directory": "{{EscapePath(Path.Combine(root, "quarantine"))}}"
              },
              "audit": {
                "log_directory": "{{EscapePath(audit)}}",
                "retain_days": 7,
                "diagnostic_mode": true
              }
            }
            """);

            var result = await RunCliAsync(configPath, CliRunTimeout, "--print-effective-config true");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"schema_version\": 1", result.Stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"hot_folder\":", result.Stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(EscapePath(hot), result.Stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CliHost_Should_Report_AutoExcluded_Subdirectories_In_ServiceStarted_Data()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-autox-" + Guid.NewGuid().ToString("N"));
        var hot = Path.Combine(root, "hot");
        var audit = Path.Combine(hot, "_audit");
        var quarantine = Path.Combine(hot, "_quarantine");
        Directory.CreateDirectory(hot);
        Directory.CreateDirectory(audit);
        Directory.CreateDirectory(quarantine);

        try
        {
            var sourceFile = Path.Combine(hot, "a.jpg");
            File.WriteAllText(sourceFile, "dummy");

            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": {
                "path": "D:\\tools\\A_system\\ExifToolGUI\\ExifTool\\ExifTool.exe",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": true,
                "extra_exiftool_args": []
              },
              "watch": {
                "hot_folder": "{{EscapePath(hot)}}",
                "include_subdirectories": true,
                "debounce_ms": 100,
                "internal_buffer_size": 65536
              },
              "rules": {
                "allowed_extensions": [".jpg"],
                "excluded_patterns": [],
                "output_mode": "same_as_source",
                "output_directory": ""
              },
              "retry": {
                "max_attempts": 1,
                "backoff_seconds": [0]
              },
              "backup": {
                "enabled": false,
                "suffix": ".bak",
                "retention": "keep"
              },
              "quarantine": {
                "enabled": true,
                "directory": "{{EscapePath(quarantine)}}"
              },
              "audit": {
                "log_directory": "{{EscapePath(audit)}}",
                "retain_days": 7,
                "diagnostic_mode": true
              }
            }
            """);

            var result = await RunCliAsync(configPath, CliRunTimeout);
            Assert.Equal(0, result.ExitCode);

            var auditFile = Directory.GetFiles(audit, "audit-*.jsonl").Single();
            var content = File.ReadAllText(auditFile);

            Assert.Contains("\"event_type\":\"service_started\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"已自动排除的子目录列表\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("_audit", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("_quarantine", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<CliRunResult> RunCliAsync(string configPath, TimeSpan timeout, string extraArgs = "", string framework = "net10.0")
    {
        var repoRoot = FindRepoRoot();
        var mergedArgs = string.IsNullOrWhiteSpace(extraArgs) ? string.Empty : " " + extraArgs.Trim();
        var psi = new ProcessStartInfo(
            "dotnet",
            $"run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj --framework {framework} -- --mode cli --config \"{configPath}\" --once true{mergedArgs}")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await WaitForExitWithTimeoutAsync(process, timeout);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new CliRunResult(process.ExitCode, stdout, stderr);
    }

    private static async Task WaitForExitWithTimeoutAsync(Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore kill race
            }

            throw new TimeoutException($"CLI process did not exit within {timeout.TotalSeconds} seconds.");
        }
    }

    private sealed record CliRunResult(int ExitCode, string Stdout, string Stderr);
}
