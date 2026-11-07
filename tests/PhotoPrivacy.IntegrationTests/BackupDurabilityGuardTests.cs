namespace PhotoPrivacy.IntegrationTests;

/// <summary>
/// #37 fsync 取舍守卫：备份写入路径的落盘步是崩溃一致性约束，设备级行为无法在单元/集成
/// 测试里观测（Flush(true) 成功与否不留任何可断言痕迹），只能由本守卫钉住实现形态保证「失效即红」。
/// </summary>
public sealed class BackupDurabilityGuardTests
{
    // 防的具体回潮形态：LocalFileOperations.CopyAsync 的 Flush(flushToDisk: true)（#37 fsync 取舍——
    // 备份是原件清洗前的唯一安全副本，只做用户态 flush 时掉电窗口内 File.Replace 会把半截/空文件
    // 换上位而原件已被清洗，损失不可逆）被当作「多余的同步等待」删掉，或 AtomicCopyAsync 不再经
    // CopyAsync 写备份副本而绕开落盘步——两种回潮都让崩溃一致性静默失效。关联 issue #37。
    [Fact]
    public void Backup_CopyAsync_Should_Flush_To_Disk_Before_Atomic_Replace()
    {
        var source = SourceLint.ReadStrippedPreservingLines(
            "src", "PhotoPrivacy.Core", "Pipeline", "LocalFileOperations.cs");

        var copyStart = source.IndexOf("async Task CopyAsync(", StringComparison.Ordinal);
        var atomicStart = source.IndexOf("async Task AtomicCopyAsync(", StringComparison.Ordinal);
        Assert.True(copyStart >= 0 && atomicStart > copyStart,
            "LocalFileOperations 失去了 CopyAsync → AtomicCopyAsync 的方法序，守卫切片失效，需先修守卫再改实现");

        var body = source[copyStart..atomicStart];
        Assert.True(body.Contains("dst.Flush(flushToDisk: true)", StringComparison.Ordinal),
            "CopyAsync 的 Flush(flushToDisk: true) 落盘步被删除——#37 fsync 取舍回退（见该方法上方 Design(#37) 标记与取舍说明）");

        Assert.True(source.Contains("await CopyAsync(source, tempPath,", StringComparison.Ordinal),
            "AtomicCopyAsync 不再经 CopyAsync 写临时副本——落盘步被绕开，#37 fsync 取舍回退");
    }
}
