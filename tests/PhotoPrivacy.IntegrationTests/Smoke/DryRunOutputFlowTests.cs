using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class DryRunOutputFlowTests
{
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
                "diagnostic_mode": false
              }
            }
            """);

            var repoRoot = FindRepoRoot();
            var psi = new ProcessStartInfo(
                "dotnet",
                $"run --project src/PhotoPrivacy.Cli/PhotoPrivacy.Cli.csproj -- --config \"{configPath}\" --once true")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);

            var copied = Path.Combine(clean, "a.jpg");
            Assert.True(File.Exists(copied));

            var auditFile = Directory.GetFiles(audit, "audit-*.jsonl").Single();
            var content = File.ReadAllText(auditFile);
            Assert.Contains("dry_run_wipe_skipped", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("file_processing_succeeded", content, StringComparison.OrdinalIgnoreCase);
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
}
