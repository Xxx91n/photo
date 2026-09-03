using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// Architecture recovery ticket 02 regression guard — src/PhotoPrivacy.Ui must never
/// launch directories/files via UseShellExecute (explorer.exe/xdg-open/open shell
/// branching). External open behavior goes through Avalonia TopLevel.Launcher
/// (LaunchDirectoryInfoAsync/LaunchFileInfoAsync). Source-lint pattern per
/// HardcodedChineseScanTests / DesignSystemTests.
/// Ticket 06 (B07) extends the literal-only guard into a whitelist: inside
/// src/PhotoPrivacy.Ui the only allowed UseShellExecute assignments are
/// <c>false</c> and the ServiceManager elevation variable form
/// (<c>needElevation</c> with Verb "runas"); anything else is red.
/// </summary>
public sealed class UiLauncherSourceTests
{

    /// <summary>
    /// Pure predicate shared by the repo-wide scan and the negative self-proof test.
    /// Returns the violation reason when <paramref name="codeLine"/> assigns
    /// UseShellExecute outside the whitelist, null when compliant (no assignment or
    /// whitelisted form). Whitelist: <c>false</c> anywhere; the elevation variable
    /// form <c>needElevation</c> only inside ServiceManager.cs.
    /// </summary>
    private static string? ClassifyUseShellExecuteLine(string codeLine, string fileName)
    {
        var idx = codeLine.IndexOf("UseShellExecute", StringComparison.Ordinal);
        if (idx < 0) return null;

        var rest = codeLine[(idx + "UseShellExecute".Length)..].TrimStart();
        if (!rest.StartsWith("=")) return null; // read sites (if (!x.UseShellExecute)) are not assignments

        var rhs = rest[1..].Trim().TrimEnd(';', ',').Trim();
        if (rhs == "false") return null;
        if (rhs == "needElevation" &&
            Path.GetFileName(fileName).Equals("ServiceManager.cs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"UseShellExecute = {rhs} in {Path.GetFileName(fileName)} " +
               "(whitelist: 'false' anywhere, 'needElevation' only in ServiceManager.cs)";
    }

    [Fact]
    public void No_UseShellExecute_True_In_Ui_Source()
    {
        var violations = new List<string>();

        foreach (var file in SourceLint.UiCsFiles())
        {
            var relative = Path.GetRelativePath(SourceLint.UiSourceRoot, file);
            foreach (var raw in File.ReadAllLines(file))
            {
                var reason = ClassifyUseShellExecuteLine(SourceLint.StripLineComment(raw), relative);
                if (reason is not null)
                {
                    violations.Add($"{Path.GetFileName(file)}: {raw.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"UseShellExecute assignments outside whitelist found in {violations.Count} locations — " +
            "only 'false' and the ServiceManager needElevation form are allowed (ticket 06 / B07):" +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void UseShellExecute_Whitelist_Negative_Self_Proof()
    {
        // Injected violating samples (in-memory, no files touched) — the classifier
        // must flag every non-whitelisted assignment form.
        var failures = new List<string>();
        var samples = new (string Line, string File, bool ExpectViolation)[]
        {
            ("            UseShellExecute = true,", "Any.cs", true),
            ("        psi.UseShellExecute = someVar;", "Other.cs", true),
            ("            UseShellExecute = needElevation,", "NotServiceManager.cs", true),
            ("            UseShellExecute = needElevation,", "Services/ServiceManager.cs", false),
            ("            UseShellExecute = false,", "Any.cs", false),
            ("        psi.UseShellExecute = false;", "Any.cs", false),
            ("            if (!startInfo.UseShellExecute)", "ServiceManager.cs", false),
            ("            // UseShellExecute = true (comment only)", "Any.cs", false),
        };

        foreach (var (line, file, expect) in samples)
        {
            var reason = ClassifyUseShellExecuteLine(SourceLint.StripLineComment(line), file);
            if (expect && reason is null)
            {
                failures.Add($"MISSED (should be red): {file}: {line.Trim()}");
            }
            else if (!expect && reason is not null)
            {
                failures.Add($"FALSE POSITIVE (should be green): {file}: {line.Trim()} -> {reason}");
            }
        }

        Assert.True(failures.Count == 0,
            $"Negative self-proof failed:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    [Fact]
    public void UseShellExecute_Whitelist_Covers_ServiceManager_Elevation_Site()
    {
        // The whitelisted elevation site must exist and keep the Verb "runas" pairing,
        // so silently removing the elevation design trips this guard too.
        var serviceManagerPath = Path.Combine(SourceLint.UiSourceRoot, "Services", "ServiceManager.cs");
        Assert.True(File.Exists(serviceManagerPath), $"ServiceManager.cs not found: {serviceManagerPath}");
        var noComments = string.Concat(File.ReadAllLines(serviceManagerPath).Select(SourceLint.StripLineComment));

        Assert.Contains("UseShellExecute = needElevation", noComments);
        Assert.Contains("needElevation ? \"runas\"", noComments);
    }

    [Fact]
    public void No_Shell_Open_Commands_In_Ui_Source()
    {
        var violations = new List<string>();
        // explorer.exe / xdg-open / open as ProcessStartInfo FileName or Process.Start
        // argument — the shell-open branching that UseShellExecute=true enabled.
        var shellOpenPattern = new Regex(@"""(explorer(\.exe)?|xdg-open|\bopen)""");

        foreach (var file in SourceLint.UiCsFiles())
        {
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = SourceLint.StripLineComment(raw);
                if (shellOpenPattern.IsMatch(line))
                {
                    violations.Add($"{Path.GetFileName(file)}: {raw.Trim()}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            $"Shell-open commands (explorer/xdg-open/open) found in {violations.Count} locations — use TopLevel.Launcher instead (ticket 02):{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Config_Dir_Open_Routes_Through_TopLevel_Launcher()
    {
        var mainWindowPath = Path.Combine(SourceLint.UiSourceRoot, "Views", "MainWindow.axaml.cs");
        Assert.True(File.Exists(mainWindowPath), $"MainWindow.axaml.cs not found: {mainWindowPath}");
        var noComments = string.Concat(File.ReadAllLines(mainWindowPath).Select(SourceLint.StripLineComment));

        Assert.Contains("LaunchDirectoryInfoAsync", noComments);
        Assert.Contains("OnOpenConfigDirClick", noComments);
    }
}
