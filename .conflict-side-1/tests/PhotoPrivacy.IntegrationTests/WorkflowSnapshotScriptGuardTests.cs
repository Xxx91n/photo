using Xunit;

namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// 票 14（B14）— WORKFLOW §4.4 轨 2 快照/校验脚本转正守卫：
/// 1) 快照/校验脚本必须以受维护身份居于 scripts/（workflow-snapshot.js / workflow-verify.js），
///    .codex-tmp 临时身份（b12-snapshot.js / b12-verify.js）不得复辟；
/// 2) 脚本形态锁定：SHA256 manifest（fileCount/totalBytes/takenAt schema）、
///    ZERO-LOSS/missing/changed/added 校验输出、零子进程（无 child_process/execSync）；
/// 3) WORKFLOW §4.4 工具段与实际一致（docs/process/WORKFLOW.md 受控副本须引用转正脚本与守卫类）；
/// 4) AGENTS.md §10 登记两脚本用法；
/// 5) 守卫自证：脚本缺位时 SourceLint.Read 必须抛出（guard 非空转）。
/// </summary>
public sealed class WorkflowSnapshotScriptGuardTests
{
    [Fact]
    public void SnapshotScript_Should_Live_In_Scripts_Without_TemporaryIdentity()
    {
        var source = SourceLint.Read("scripts", "workflow-snapshot.js");
        Assert.Contains("manifest.json", source, StringComparison.Ordinal);
        Assert.Contains("createHash", source, StringComparison.Ordinal);
        Assert.DoesNotContain("child_process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("execSync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotScript_Should_Emit_Schema_Compatible_Manifest()
    {
        // manifest schema 与既有快照标本（20260902-074101..083352）逐字段一致。
        var source = SourceLint.Read("scripts", "workflow-snapshot.js");
        Assert.Contains("fileCount", source, StringComparison.Ordinal);
        Assert.Contains("totalBytes", source, StringComparison.Ordinal);
        Assert.Contains("takenAt", source, StringComparison.Ordinal);
        Assert.Contains("sha256", source, StringComparison.Ordinal);
        Assert.Contains("files:", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VerifyScript_Should_Output_Missing_Changed_Added_And_ZeroLoss()
    {
        var source = SourceLint.Read("scripts", "workflow-verify.js");
        Assert.Contains("ZERO-LOSS", source, StringComparison.Ordinal);
        Assert.Contains("MISSING", source, StringComparison.Ordinal);
        Assert.Contains("CHANGED", source, StringComparison.Ordinal);
        Assert.Contains("ADDED", source, StringComparison.Ordinal);
        Assert.Contains("manifest.json", source, StringComparison.Ordinal);
        Assert.DoesNotContain("child_process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("execSync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexTmp_Should_Not_Revive_Temporary_Snapshot_Scripts()
    {
        var legacyDir = Path.Combine(SourceLint.RepoRoot, ".codex-tmp");
        Assert.False(File.Exists(Path.Combine(legacyDir, "b12-snapshot.js")),
            ".codex-tmp/b12-snapshot.js 不得复辟；轨 2 工具已转正 scripts/workflow-snapshot.js");
        Assert.False(File.Exists(Path.Combine(legacyDir, "b12-verify.js")),
            ".codex-tmp/b12-verify.js 不得复辟；轨 2 工具已转正 scripts/workflow-verify.js");
    }

    [Fact]
    public void WorkflowToolParagraph_Should_Match_Promoted_Reality()
    {
        // 红线：WORKFLOW §4.4 工具段与实际一致（受版本控制的 docs/process 副本为断言对象）。
        var workflow = SourceLint.Read("docs", "process", "WORKFLOW.md");
        Assert.Contains("scripts/workflow-snapshot.js", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts/workflow-verify.js", workflow, StringComparison.Ordinal);
        Assert.Contains("WorkflowSnapshotScriptGuardTests", workflow, StringComparison.Ordinal);
        Assert.Contains("临时身份就此退役", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentsRegistry_Should_List_Promoted_Tools()
    {
        var agents = SourceLint.Read("AGENTS.md");
        var sectionStart = agents.IndexOf("## 10. 外部工具路径", StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, "AGENTS.md 缺少 §10 外部工具路径段");
        var toolsSection = agents[sectionStart..];
        Assert.Contains("workflow-snapshot.js", toolsSection, StringComparison.Ordinal);
        Assert.Contains("workflow-verify.js", toolsSection, StringComparison.Ordinal);
        Assert.Contains("node scripts/workflow-snapshot.js", toolsSection, StringComparison.Ordinal);
        Assert.Contains("node scripts/workflow-verify.js", toolsSection, StringComparison.Ordinal);
    }

    [Fact]
    public void GuardHarness_Should_Bite_On_Missing_Script()
    {
        // 守卫自证：脚本缺位时 SourceLint.Read 必须抛出，防止断言空转。
        Assert.ThrowsAny<FileNotFoundException>(
            () => SourceLint.Read("scripts", "workflow-snapshot.definitely-absent.js"));
    }
}
