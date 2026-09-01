using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// Architecture recovery ticket 02 regression guard — src/PhotoPrivacy.Ui must never
/// launch directories/files via UseShellExecute (explorer.exe/xdg-open/open shell
/// branching). External open behavior goes through Avalonia TopLevel.Launcher
/// (LaunchDirectoryInfoAsync/LaunchFileInfoAsync). Source-lint pattern per
/// HardcodedChineseScanTests / DesignSystemTests.
/// </summary>
public sealed class UiLauncherSourceTests
{
    private static readonly string UiSourceRoot = ResolveUiSourceRoot();

    private static string ResolveUiSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        var current = dir;
        for (var i = 0; i < 8; i++)
        {
            current = Path.GetFullPath(Path.Combine(current, ".."));
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                File.Exists(Path.Combine(current, "PhotoPrivacy.sln")))
            {
                return Path.Combine(current, "src", "PhotoPrivacy.Ui");
            }
        }
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "..", "src", "PhotoPrivacy.Ui"));
    }

    private static List<string> GetUiCsFiles()
    {
        Assert.True(Directory.Exists(UiSourceRoot), $"Ui source root not found: {UiSourceRoot}");
        return Directory.GetFiles(UiSourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.EndsWith(".g.cs") && !f.EndsWith(".GlobalUsings.g.cs"))
            .ToList();
    }

    [Fact]
    public void No_UseShellExecute_True_In_Ui_Source()
    {
        var violations = new List<string>();

        foreach (var file in GetUiCsFiles())
        {
            var lines = File.ReadAllLines(file);
            foreach (var raw in lines)
            {
                var line = raw;
                var commentIdx = line.IndexOf("//");
                if (commentIdx >= 0) line = line[..commentIdx];

                if (line.Contains("UseShellExecute = true"))
                {
                    violations.Add($"{Path.GetFileName(file)}: {raw.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"UseShellExecute=true found in {violations.Count} locations — use TopLevel.Launcher instead (ticket 02):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void No_Shell_Open_Commands_In_Ui_Source()
    {
        var violations = new List<string>();
        // explorer.exe / xdg-open / open as ProcessStartInfo FileName or Process.Start
        // argument — the shell-open branching that UseShellExecute=true enabled.
        var shellOpenPattern = new Regex(@"""(explorer(\.exe)?|xdg-open|\bopen)""");

        foreach (var file in GetUiCsFiles())
        {
            var lines = File.ReadAllLines(file);
            foreach (var raw in lines)
            {
                var line = raw;
                var commentIdx = line.IndexOf("//");
                if (commentIdx >= 0) line = line[..commentIdx];

                foreach (Match m in shellOpenPattern.Matches(line))
                {
                    violations.Add($"{Path.GetFileName(file)}: {raw.Trim()}");
                    break;
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Shell-open commands (explorer/xdg-open/open) found in {violations.Count} locations — use TopLevel.Launcher instead (ticket 02):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Config_Dir_Open_Routes_Through_TopLevel_Launcher()
    {
        var mainWindowPath = Path.Combine(UiSourceRoot, "Views", "MainWindow.axaml.cs");
        Assert.True(File.Exists(mainWindowPath), $"MainWindow.axaml.cs not found: {mainWindowPath}");
        var noComments = string.Concat(File.ReadAllLines(mainWindowPath)
            .Select(l => { var i = l.IndexOf("//"); return i >= 0 ? l[..i] : l; }));

        Assert.Contains("LaunchDirectoryInfoAsync", noComments);
        Assert.Contains("OnOpenConfigDirClick", noComments);
    }
}
