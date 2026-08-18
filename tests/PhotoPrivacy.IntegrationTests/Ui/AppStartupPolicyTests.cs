using System.Text;
using System.Text.RegularExpressions;

namespace PhotoPrivacy.IntegrationTests.Ui;

public sealed class AppStartupPolicyTests
{
    [Fact]
    public void App_Source_Should_Not_Always_Hide_Window_Based_On_HideMainWindowOnStartup_Alone()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "App.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.DoesNotContain("if (RuntimeOptions.HideMainWindowOnStartup)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0053 M1 regression guard: Program.cs Start method must NOT contain
    /// sync-over-async (.GetAwaiter().GetResult() / .Result / .Wait()) in the
    /// startup path — the blocking ConnectOrLaunchAsync call was replaced with
    /// Start(AppMain) two-phase async startup.
    /// </summary>
    [Fact]
    public void Program_Start_Method_Should_Not_Block_On_Worker_Connect()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Program.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.Contains("Start(AppMain", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartWithClassicDesktopLifetime", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run(async", source, StringComparison.Ordinal);
        Assert.Contains("ConnectOrLaunchWorkerAsync", source, StringComparison.Ordinal);

        var startIdx = source.IndexOf("public static int Start(string[] args)");
        Assert.True(startIdx >= 0, "Start method not found in Program.cs");
        var startBlock = source.Substring(startIdx, 3000);

        var appMainIdx = startBlock.IndexOf("private static void AppMain");
        var startMethodOnly = appMainIdx > 0
            ? startBlock.Substring(0, appMainIdx)
            : startBlock.Substring(0, 2500);

        Assert.DoesNotContain("ConnectOrLaunchAsync(", startMethodOnly, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR 0053 M2 regression guard: MainWindow.axaml.cs must contain ZERO
    /// sync-over-async calls (.GetAwaiter().GetResult() / .Result / .Wait()).
    /// </summary>
    [Fact]
    public void MainWindow_Should_Have_Zero_SyncOverAsync()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Views", "MainWindow.axaml.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        Assert.DoesNotContain("GetAwaiter().GetResult()", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Wait()", source, StringComparison.Ordinal);

        var lines = source.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/*")) continue;
            if (trimmed.Contains(".Result") && !trimmed.Contains("Results"))
            {
                Assert.Fail("sync-over-async .Result found: " + trimmed);
            }
        }
    }
}
