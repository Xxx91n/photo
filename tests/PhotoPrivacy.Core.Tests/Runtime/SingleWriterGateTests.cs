using PhotoPrivacy.Core.Runtime;

namespace PhotoPrivacy.Core.Tests.Runtime;

public sealed class SingleWriterGateTests
{
    [Fact]
    public void TryAcquire_FirstAcquires_SecondYields()
    {
        var dir = NewTempAuditDir();
        try
        {
            var first = SingleWriterGate.TryAcquire(dir);
            Assert.NotNull(first.Guard);
            Assert.Null(first.Error);
            Assert.Equal(SingleWriterGate.ResolveEffectiveAuditDirectory(dir), first.AuditDirectory);

            var second = SingleWriterGate.TryAcquire(dir);
            Assert.Null(second.Guard);
            Assert.Null(second.Error);
            Assert.Equal(first.AuditDirectory, second.AuditDirectory);
            first.Guard.Dispose();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryAcquire_AfterRelease_IsReacquirable()
    {
        var dir = NewTempAuditDir();
        try
        {
            var first = SingleWriterGate.TryAcquire(dir);
            Assert.NotNull(first.Guard);
            first.Guard.Dispose();

            var second = SingleWriterGate.TryAcquire(dir);
            Assert.NotNull(second.Guard);
            Assert.Null(second.Error);
            second.Guard.Dispose();
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryAcquire_DifferentDirectories_Coexist()
    {
        var dirA = NewTempAuditDir();
        var dirB = NewTempAuditDir();
        try
        {
            var a = SingleWriterGate.TryAcquire(dirA);
            var b = SingleWriterGate.TryAcquire(dirB);
            try
            {
                Assert.NotNull(a.Guard);
                Assert.NotNull(b.Guard);
                Assert.NotEqual(a.AuditDirectory, b.AuditDirectory);
            }
            finally
            {
                a.Guard?.Dispose();
                b.Guard?.Dispose();
            }
        }
        finally
        {
            TryDeleteDir(dirA);
            TryDeleteDir(dirB);
        }
    }

    [Fact]
    public void ResolveEffectiveAuditDirectory_Empty_FallsBackToBaseDirectoryAudit()
    {
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "_audit"),
            SingleWriterGate.ResolveEffectiveAuditDirectory(""));
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "_audit"),
            SingleWriterGate.ResolveEffectiveAuditDirectory("   "));
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "_audit"),
            SingleWriterGate.ResolveEffectiveAuditDirectory(null));
        var configured = Path.Combine(Path.GetTempPath(), "configured-audit");
        Assert.Equal(configured, SingleWriterGate.ResolveEffectiveAuditDirectory(configured));
    }

    [Fact]
    public void TryAcquire_InvalidPath_ReturnsErrorInsteadOfThrowing()
    {
        // 防：删除加载期建目录后，非法审计路径不再在配置加载处早失败；异常若逃出准入门，
        // 会在 Program 注册 UnhandledException 之前崩掉 Worker（#153）。
        var admission = SingleWriterGate.TryAcquire("audit\0dir");

        Assert.Null(admission.Guard);
        Assert.NotNull(admission.Error);
    }

    [Fact]
    public void BuildLockName_Stable_AndDistinctPerDirectory()
    {
        var a = Path.Combine(Path.GetTempPath(), "swg-a");
        var b = Path.Combine(Path.GetTempPath(), "swg-b");
        Assert.Equal(SingleWriterGate.BuildLockName(a), SingleWriterGate.BuildLockName(a));
        Assert.NotEqual(SingleWriterGate.BuildLockName(a), SingleWriterGate.BuildLockName(b));
        Assert.StartsWith("Global\\PhotoPrivacyWorker_Audit_", SingleWriterGate.BuildLockName(a));
    }

    [Fact]
    public void BuildLockName_WindowsPaths_FoldCaseOnlyOnWindows()
    {
        var lower = Path.Combine(Path.GetTempPath().ToLowerInvariant(), "swg-case");
        var upper = Path.Combine(Path.GetTempPath().ToUpperInvariant(), "SWG-CASE");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(SingleWriterGate.BuildLockName(lower), SingleWriterGate.BuildLockName(upper));
        }
        else
        {
            Assert.NotEqual(SingleWriterGate.BuildLockName(lower), SingleWriterGate.BuildLockName(upper));
        }
    }

    [Fact]
    public void ResolveEffectiveAuditDirectory_SymlinkAlias_ResolvesToSameIdentity()
    {
        // 防回归点（CodeRabbit #163）：锁身份必须跟物理目录走，别名路径不得各持一锁绕过准入门。
        // Windows 建 symlink 需要特权，此断言只在 POSIX 上跑；Windows 的 junction 规则由实现内
        // ResolveLinkTarget 同一通道覆盖，不另做特权测试。
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var real = NewTempAuditDir();
        var alias = real + "-alias";
        Directory.CreateSymbolicLink(alias, real);
        try
        {
            var viaReal = SingleWriterGate.ResolveEffectiveAuditDirectory(real);
            var viaAlias = SingleWriterGate.ResolveEffectiveAuditDirectory(alias);
            Assert.Equal(SingleWriterGate.BuildLockName(viaReal), SingleWriterGate.BuildLockName(viaAlias));
        }
        finally
        {
            TryDeleteDir(real);
            try { Directory.Delete(alias); } catch { /* best effort */ }
        }
    }

    private static string NewTempAuditDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phprvtest-swg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }
}
