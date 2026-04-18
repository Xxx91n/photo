using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class InstanceConflictAuditTests
{
    [Fact]
    public async Task Cli_Should_Write_InstanceConflict_Audit_When_Duplicate_Instance_Detected()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-instance-conflict-" + Guid.NewGuid().ToString("N"));
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
                "directory": "{{EscapePath(Path.Combine(root, "quarantine"))}}"
              },
              "audit": {
                "log_directory": "{{EscapePath(audit)}}",
                "retain_days": 7,
                "diagnostic_mode": false
              }
            }
            """);

            var repoRoot = FindRepoRoot();
            using var owner = StartCli(repoRoot, configPath);
            await Task.Delay(1500);

            var conflict = await RunCliOnceAsync(repoRoot, configPath, TimeSpan.FromSeconds(30));
            Assert.Equal(1, conflict.ExitCode);
            Assert.Contains("另一个实例已在运行", conflict.Stdout + conflict.Stderr, StringComparison.OrdinalIgnoreCase);

            owner.Kill(entireProcessTree: true);
            await owner.WaitForExitAsync();

            var auditFile = Directory.GetFiles(audit, "audit-*.jsonl").Single();
            var content = File.ReadAllText(auditFile);
            Assert.Contains("\"event_type\":\"instance_conflict\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("检测到重复启动并已拒绝", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static Process StartCli(string repoRoot, string configPath)
    {
        var psi = new ProcessStartInfo(
            "dotnet",
            $"run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj --framework net10.0 -- --mode cli --config \"{configPath}\"")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        return Process.Start(psi)!;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliOnceAsync(string repoRoot, string configPath, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(
            "dotnet",
            $"run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj --framework net10.0 -- --mode cli --once true --config \"{configPath}\"")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Conflict run timed out.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
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
}
