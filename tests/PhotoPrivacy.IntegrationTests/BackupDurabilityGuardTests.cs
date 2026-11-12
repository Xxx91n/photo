namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// #229 fsync 取舍检查（原登记 #37）：备份写入路径的落盘步是崩溃一致性约束，设备级行为无法在单元/集成
/// 测试里观测（Flush(true) 成功与否不留任何可断言痕迹），只能由本检查钉住实现形态保证「失效即红」。
/// </summary>
public sealed class BackupDurabilityGuardTests
{
    [Fact]
    public void Backup_CopyAsync_Should_Flush_To_Disk_Before_Atomic_Replace()
    {
        // 要防的老毛病：LocalFileOperations.CopyAsync 的 Flush(flushToDisk: true)（#229 fsync 取舍——
        // 备份是原件清洗前的唯一安全副本，只做用户态 flush 时掉电窗口内 File.Replace 会把半截/空文件
        // 换上位而原件已被清洗，损失不可逆）被当作「多余的同步等待」删掉，或 AtomicCopyAsync 不再经
        // CopyAsync 写备份副本而绕开落盘步——两种老毛病再犯都让崩溃一致性静默失效。关联 issue #37。
        Assert.Empty(ClassifyBackupDurabilityViolations(ReadStrippedLocalFileOperations()));
    }

    [Fact]
    public void Backup_Durability_Guard_Negative_Self_Proof()
    {
        // 失效即红自我验证：绿态零违规；① 删掉 CopyAsync 的 Flush(flushToDisk: true) 落盘行、
        // ② AtomicCopyAsync 不再经 CopyAsync 写临时副本（内联复制绕开落盘步），两种老毛病形态
        // 都必须报红——防「落盘步被删而检查仍绿」的假通过事故。关联 issue #37。
        var green = ReadStrippedLocalFileOperations();
        Assert.Empty(ClassifyBackupDurabilityViolations(green));

        var withoutFlush = string.Join(
            "\n",
            green.Split('\n').Where(line => !line.Contains("dst.Flush(flushToDisk: true)", StringComparison.Ordinal)));
        Assert.NotEmpty(ClassifyBackupDurabilityViolations(withoutFlush));

        var bypassingCopyAsync = green.Replace(
            "await CopyAsync(source, tempPath, overwrite: true, cancellationToken).ConfigureAwait(false);",
            "var __bypassed = source;",
            StringComparison.Ordinal);
        Assert.NotEmpty(ClassifyBackupDurabilityViolations(bypassingCopyAsync));
    }

    /// <summary>
    /// 检查样本：走 <see cref="SourceLint.ReadStrippedPreservingLines"/>（共享读取 + 剥注释，
    /// 保留行结构）——行级增删变异样本需要换行，<c>ReadStripped</c> 不含换行无法支撑。
    /// </summary>
    private static string ReadStrippedLocalFileOperations()
        => SourceLint.ReadStrippedPreservingLines(
            "src", "PhotoPrivacy.Core", "Pipeline", "LocalFileOperations.cs");

    /// <summary>零违规 = 绿态：CopyAsync → AtomicCopyAsync 方法序在、落盘步在 CopyAsync 体内、临时副本经 CopyAsync 写。</summary>
    private static List<string> ClassifyBackupDurabilityViolations(string source)
    {
        var violations = new List<string>();

        var copyStart = source.IndexOf("async Task CopyAsync(", StringComparison.Ordinal);
        var atomicStart = source.IndexOf("async Task AtomicCopyAsync(", StringComparison.Ordinal);
        if (copyStart < 0 || atomicStart <= copyStart)
        {
            violations.Add("LocalFileOperations 失去了 CopyAsync → AtomicCopyAsync 的调用顺序，检查前提失效，需先修检查再改实现");
            return violations;
        }

        if (!source[copyStart..atomicStart].Contains("dst.Flush(flushToDisk: true)", StringComparison.Ordinal))
        {
            violations.Add("CopyAsync 的 Flush(flushToDisk: true) 落盘步被删除——#229 fsync 取舍回退（见该方法上方的落盘取舍说明）");
        }

        // 「经 CopyAsync 写副本」判定限 AtomicCopyAsync 方法体切片（到 MoveAsync 声明为止），
        // 不用整文件 Contains——别处出现同名字符串时不应误判为通过。
        var moveStart = source.IndexOf("Task MoveAsync(", StringComparison.Ordinal);
        var atomicBody = moveStart > atomicStart ? source[atomicStart..moveStart] : source[atomicStart..];
        if (!atomicBody.Contains("await CopyAsync(source, tempPath,", StringComparison.Ordinal))
        {
            violations.Add("AtomicCopyAsync 不再经 CopyAsync 写临时副本——落盘步被绕开，#229 fsync 取舍回退");
        }

        return violations;
    }
}
