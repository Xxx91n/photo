using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class InstanceConflictAuditTests : IntegrationTestBase
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
            var exifToolPath = RequireExifTool();
            var configPath = Path.Combine(root, "config.json");
            File.WriteAllText(configPath, $$"""
            {
              "schema_version": 1,
              "exiftool": {
                "path": "{{EscapePath(exifToolPath)}}",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": true,
                "extra_exiftool_args": [],
                "stay_open_pool_size": 1,
                "max_parallel_drain": 1
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
                "diagnostic_mode": true
              }
            }
            """);

            var repoRoot = FindRepoRoot();
            using var owner = StartCli(repoRoot, configPath);
            WaitForAuditEvent(audit, "service_started", TimeSpan.FromSeconds(15));

            var conflict = await RunCliOnceAsync(repoRoot, configPath, TimeSpan.FromSeconds(30));
            Assert.Equal(1, conflict.ExitCode);
            Assert.NotEmpty(conflict.Stderr);

            owner.Kill(entireProcessTree: true);
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await owner.WaitForExitAsync(exitCts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("CLI owner process did not exit within 10s after tree-kill.");
            }

            var auditFile = WaitForAuditFile(audit, TimeSpan.FromSeconds(5));
            var content = File.ReadAllText(auditFile);
            Assert.Contains("\"event_type\":\"instance_conflict\"", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("检测到重复启动并已拒绝", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                SafeDeleteDirectory(root);
            }
        }
    }

    [Fact]
    public async Task WaitForAuditEvent_Should_Retry_When_Audit_File_Is_Temporarily_Locked()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-instance-conflict-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var auditFile = Path.Combine(root, $"audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
        await File.WriteAllTextAsync(auditFile, "{\"event_type\":\"service_started\"}" + Environment.NewLine);

        FileStream? hold = null;
        try
        {
            hold = new FileStream(auditFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var waitTask = Task.Run(() => WaitForAuditEvent(root, "service_started", TimeSpan.FromSeconds(3)));

            await Task.Delay(300);
            hold.Dispose();
            hold = null;

            await waitTask;
        }
        finally
        {
            hold?.Dispose();

            if (Directory.Exists(root))
            {
                SafeDeleteDirectory(root);
            }
        }
    }

    private static Process StartCli(string repoRoot, string configPath)
    {
        var workerDll = Path.Combine(repoRoot, "src", "PhotoPrivacy.Worker", "bin", "Debug", "net10.0", "PhotoPrivacyWorker.dll");
        if (!File.Exists(workerDll))
        {
            throw new FileNotFoundException(
                $"Worker DLL not found: {workerDll}. Test hosts are DLL-first; build it first with: dotnet build PhotoPrivacy.sln",
                workerDll);
        }

        var psi = new ProcessStartInfo("dotnet", $"\"{workerDll}\" --mode cli --config \"{configPath}\"")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        return Process.Start(psi)!;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliOnceAsync(string repoRoot, string configPath, TimeSpan timeout)
    {
        var workerDll = Path.Combine(repoRoot, "src", "PhotoPrivacy.Worker", "bin", "Debug", "net10.0", "PhotoPrivacyWorker.dll");
        if (!File.Exists(workerDll))
        {
            throw new FileNotFoundException(
                $"Worker DLL not found: {workerDll}. Test hosts are DLL-first; build it first with: dotnet build PhotoPrivacy.sln",
                workerDll);
        }

        var psi = new ProcessStartInfo("dotnet", $"\"{workerDll}\" --mode cli --once true --config \"{configPath}\"")
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

    private static string WaitForAuditFile(string auditDirectory, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var file = Directory.GetFiles(auditDirectory, "audit-*.jsonl").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(file))
            {
                return file;
            }

            Thread.Sleep(100);
        }

        throw new FileNotFoundException("instance_conflict audit file not found", auditDirectory);
    }

    private static void SafeDeleteDirectory(string directoryPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(directoryPath, recursive: true);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }

        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void WaitForAuditEvent(string auditDirectory, string eventType, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var file = Directory.GetFiles(auditDirectory, "audit-*.jsonl").FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(file))
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains($"\"event_type\":\"{eventType}\"", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                    // audit writer may hold a short lock while appending; retry until timeout
                }
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"audit event '{eventType}' not found within timeout.");
    }
}
