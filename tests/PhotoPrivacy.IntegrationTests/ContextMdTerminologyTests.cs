using System.Text;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// Ticket 21 source-lint guard for the CONTEXT.md three-layer consistency closeout.
/// Locks the authoritative terminology table against the nine drifts fixed in this ticket:
/// the new Endpoint Ownership term, the owner-conditional Stale Socket Cleanup wording, the
/// non-empty (not "absolute path") Hot Folder Guard, the dual-socket Unix Domain Socket, the
/// implemented Theme Swatch Grid, version-via-GetStatusAsync Typed WorkerIpcClient, the
/// four-button Sidebar Nav, and the bak (not .pp_backup) backup default directory.
/// Uses SourceLint.RepoRoot to locate CONTEXT.md at the repo root.
/// </summary>
public sealed class ContextMdTerminologyTests
{
    private static string ReadContextMd() =>
        File.ReadAllText(Path.Combine(SourceLint.RepoRoot, "CONTEXT.md"), Encoding.UTF8);

    private static void AssertContains(string expected) =>
        Assert.Contains(expected, ReadContextMd(), StringComparison.Ordinal);

    private static void AssertDoesNotContain(string forbidden) =>
        Assert.DoesNotContain(forbidden, ReadContextMd(), StringComparison.Ordinal);

    [Fact]
    public void EndpointOwnership_Term_Should_Exist()
    {
        AssertContains("**Endpoint Ownership**");
        AssertContains("ownsEndpoint = _listener is not null");
    }

    [Fact]
    public void StaleSocketCleanup_Should_Distinguish_Owner_Conditional_Dispose()
        => AssertContains("DisposeAsync 的 socket 删除属主条件");

    [Fact]
    public void HotFolderGuard_Should_Require_NonEmpty_Not_AbsolutePath()
    {
        AssertContains("hot_folder 必须为非空");
        AssertDoesNotContain("非空绝对路径");
    }

    [Fact]
    public void UnixDomainSocket_Should_Mention_Dual_Socket()
        => AssertContains("worker-background.sock");

    [Fact]
    public void ThemeSwatchGrid_Should_Be_Implemented()
    {
        AssertContains("已有 swatch grid UI 入口");
        AssertDoesNotContain("已有但无 UI 入口");
    }

    [Fact]
    public void TypedWorkerIpcClient_Should_Get_Version_Via_GetStatusAsync()
    {
        AssertContains("exiftool 版本经 GetStatusAsync");
        AssertDoesNotContain("GetRecentLogs/GetExifToolVersion");
    }

    [Fact]
    public void SidebarNav_Should_Have_Four_Buttons()
    {
        AssertContains("4 按钮（Config/Logs/Rules/ServiceManager）");
        AssertDoesNotContain("三按钮（Config/Logs/ServiceManager）");
    }

    [Fact]
    public void DefaultBackupDirectory_Should_Be_Bak_Not_PpBackup()
    {
        AssertContains("`<hotFolder>/bak`");
        AssertDoesNotContain("`<hotFolder>/.pp_backup`");
    }
}
