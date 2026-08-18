using System.Text;

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
    /// Start(AppMain) two-phase async startup. The only allowed GetAwaiter().GetResult()
    /// is the post-lifetime cleanup of showPipeTask (line ~89), which runs after
    /// Avalonia has exited and is safe.
    /// </summary>
    [Fact]
    public void Program_Start_Method_Should_Not_Block_On_Worker_Connect()
    {
        var sourcePath = Path.Combine("D:", "Aworker", "photo", "src", "PhotoPrivacy.Ui", "Program.cs");
        var source = File.ReadAllText(sourcePath, Encoding.UTF8);

        // Verify Start(AppMain) is used instead of StartWithClassicDesktopLifetime
        Assert.Contains("Start(AppMain", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartWithClassicDesktopLifetime", source, StringComparison.Ordinal);

        // Verify fire-and-forget background Worker connect
        Assert.Contains("Task.Run(async", source, StringComparison.Ordinal);
        Assert.Contains("ConnectOrLaunchWorkerAsync", source, StringComparison.Ordinal);

        // The Start method block must not contain blocking Worker connect
        var startIdx = source.IndexOf("public static int Start(string[] args)");
        Assert.True(startIdx >= 0, "Start method not found in Program.cs");
        var startBlock = source.Substring(startIdx, 3000);

        // Find the AppMain method boundary to exclude it from the check
        var appMainIdx = startBlock.IndexOf("private static void AppMain");
        var startMethodOnly = appMainIdx > 0
            ? startBlock.Substring(0, appMainIdx)
            : startBlock.Substring(0, 2500);

        // Start method must not block on ConnectOrLaunchAsync before Avalonia starts
        Assert.DoesNotContain(
            "ConnectOrLaunchAsync(",
            startMethodOnly.Split("GetAwaiter().GetResult()")[0],
            StringComparison.Ordinal);
    }
}
