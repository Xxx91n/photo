namespace PhotoPrivacy.IntegrationTests.Smoke;

/// <summary>
/// 票 16 — release-readiness.ps1 四步全链路捕获扩展回归锁定：
/// [1/3] dotnet test 与 [4/4] force-kill 两步纳入 Invoke-ScriptWithCapture（复用票09 捕获机制），
/// 四步（dotnet test / smoke / publish / force-kill）全部经捕获调用，失败自带 stdout/stderr 尾部；
/// 不再存在直连流式调用形态（dotnet test 直连、powershell -File 直连 force-kill）。
/// 独立于 SmokeScriptDiagnosticsTests.cs：票15 在途改同文件，本 guard 按 §4.3 放独立文件，避免共享文件冲突。
/// </summary>
public sealed class ReleaseReadinessFullCaptureGuardTests
{
    [Fact]
    public void ReleaseReadiness_Should_Capture_All_Four_Steps()
    {
        var source = SourceLint.Read("scripts", "release-readiness.ps1");

        // 四步均经捕获函数（label 即步骤名，函数定义只出现一次，调用点各一次）
        Assert.Contains("Invoke-ScriptWithCapture -Label \"dotnet test\"", source, StringComparison.Ordinal);
        Assert.Contains("Invoke-ScriptWithCapture -Label \"smoke.ps1\"", source, StringComparison.Ordinal);
        Assert.Contains("Invoke-ScriptWithCapture -Label \"publish-app.ps1\"", source, StringComparison.Ordinal);
        Assert.Contains("Invoke-ScriptWithCapture -Label \"verify-force-kill-cleanup.ps1\"", source, StringComparison.Ordinal);

        // 直连调用形态清零：不再直接执行 dotnet test / powershell -File 直启 force-kill 脚本
        Assert.DoesNotContain("dotnet test \"$repoRoot\\PhotoPrivacy.sln\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("-File \"$repoRoot\\scripts\\verify-force-kill-cleanup.ps1\"", source, StringComparison.Ordinal);
    }
}
