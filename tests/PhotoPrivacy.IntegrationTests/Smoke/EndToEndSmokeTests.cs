using System.Diagnostics;

namespace PhotoPrivacy.IntegrationTests.Smoke;

public sealed class EndToEndSmokeTests : IntegrationTestBase
{
    [Fact]
    public async Task CliHost_Should_Process_One_File_And_Write_Audit_Event()
    {
        var root = Path.Combine(Path.GetTempPath(), "photo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var repoRoot = FindRepoRoot();
            var hot = Path.Combine(root, "hot");
            var audit = Path.Combine(root, "audit");
            Directory.CreateDirectory(hot);
            Directory.CreateDirectory(audit);
            var exifToolPath = RequireExifTool();
            File.WriteAllText(Path.Combine(hot, "a.jpg"), "dummy");

            var script = Path.Combine(repoRoot, "scripts", "smoke.ps1");
            var psi = new ProcessStartInfo("powershell", $"-ExecutionPolicy Bypass -File \"{script}\" -HotFolder \"{hot}\" -AuditFolder \"{audit}\" -ExifToolPath \"{exifToolPath}\"")
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            Assert.Equal(0, p.ExitCode);

            var auditFile = Directory.GetFiles(audit, "audit-*.jsonl").Single();
            var lines = File.ReadAllLines(auditFile);
            Assert.Contains(lines, l => l.Contains("file_processing_succeeded", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
