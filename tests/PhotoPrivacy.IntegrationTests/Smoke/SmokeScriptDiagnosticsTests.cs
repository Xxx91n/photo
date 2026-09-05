using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

/// <summary>
/// 票 09（B09+B13）— smoke.ps1 DLL-first 与失败诊断回归锁定：
/// 1) smoke.ps1 无 dotnet run 残留且直启 PhotoPrivacyWorker.dll（source guard）；
/// 2) release-readiness.ps1 对 smoke/publish 子脚本做输出捕获并打印尾部（source guard）；
/// 3) 冒烟失败路径诊断含子进程 stdout/stderr 尾部（行为测试，坏 exiftool 路径触发校验失败）。
/// 4) 票15 — tests/ 直启宿主（InstanceConflictAuditTests/DryRunOutputFlowTests）无 dotnet run
///    兜底（含拆参形态），DLL-first 直启 + DLL 缺失显式报错（source guard 扩域）。
/// </summary>
[Trait("Category", "Smoke")]
public sealed class SmokeScriptDiagnosticsTests
{
    [Fact]
    public void SmokeScript_Should_Be_DllFirst_Without_DotnetRun()
    {
        var source = SourceLint.Read("scripts", "smoke.ps1");
        Assert.DoesNotContain("dotnet run", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet-run", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PhotoPrivacyWorker.dll", source, StringComparison.Ordinal);
    }

    // 拆参形态兜底标记（fileName="dotnet"，arguments 以 "run … --project …" 开头）。标记拆写拼接，避免守卫源码自引用命中全树扫描。
    private const string FallbackMarker = "run --" + "project";

    [Fact]
    public void CliTestHosts_Should_Be_DllFirst_Without_DotnetRunFallback()
    {
        // 票 15 — guard 从 scripts/ 扩域到 tests/ 直启宿主：
        // 1) 全 tests/ 源码树无拆参形态 dotnet run 兜底（排除 bin/obj）；
        // 2) 两个直启宿主保留 DLL-first 直启与 DLL 缺失构建指引，不再有 dotnet run。
        var testsRoot = Path.Combine(SourceLint.RepoRoot, "tests");
        var offenders = Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains(FallbackMarker, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(offenders.Count == 0,
            "dotnet run fallback (split-form) found in tests/: " + string.Join(", ", offenders));

        foreach (var host in new[] { "InstanceConflictAuditTests.cs", "DryRunOutputFlowTests.cs" })
        {
            var source = SourceLint.Read("tests", "PhotoPrivacy.IntegrationTests", "Smoke", host);
            Assert.DoesNotContain("dotnet run", source, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PhotoPrivacyWorker.dll", source, StringComparison.Ordinal);
            Assert.Contains("dotnet build", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReleaseReadinessScript_Should_Capture_Child_Output_And_Print_Tails()
    {
        var source = SourceLint.Read("scripts", "release-readiness.ps1");
        // 子脚本捕获 + 尾部诊断标记必须覆盖 smoke 与 publish 两条失败路径。
        Assert.Contains("Invoke-ScriptWithCapture", source, StringComparison.Ordinal);
        Assert.Contains("stdout (tail)", source, StringComparison.Ordinal);
        Assert.Contains("stderr (tail)", source, StringComparison.Ordinal);
        Assert.Contains("smoke.ps1", source, StringComparison.Ordinal);
        Assert.Contains("publish-app.ps1", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SmokeScript_Should_Include_Worker_Output_Tails_In_Failure_Diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-smoke-diag-" + Guid.NewGuid().ToString("N"));
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
                "path": "{{EscapePath(Path.Combine(root, "no-such-dir", "ExifTool.exe"))}}",
                "enable_windows_long_path": true,
                "enable_large_file_support": true,
                "dry_run": false,
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
                "suffix": ".bak"
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

            var repoRoot = SourceLint.RepoRoot;
            var script = Path.Combine(repoRoot, "scripts", "smoke.ps1");
            var psi = new ProcessStartInfo("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -HotFolder \"{hot}\" -AuditFolder \"{audit}\" -ConfigPath \"{configPath}\" -DryRun false")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(psi)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("smoke.ps1 failure-diagnostic probe did not exit within 120s.");
            }
            var merged = await stdoutTask + Environment.NewLine + await stderrTask;

            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("exiftool.path not found", merged, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("worker stdout (tail)", merged, StringComparison.Ordinal);
            Assert.Contains("worker stderr (tail)", merged, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string EscapePath(string path) => path.Replace("\\", "\\\\");
}
