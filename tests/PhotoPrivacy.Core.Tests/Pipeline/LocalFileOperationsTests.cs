using PhotoPrivacy.Core.Pipeline;

namespace PhotoPrivacy.Core.Tests.Pipeline;

public sealed class LocalFileOperationsTests
{
    [Fact]
    public void Move_Should_Fallback_To_Copy_And_Delete_When_Primary_Move_Throws_IOException()
    {
        var copied = false;
        var deleted = false;

        var ops = new LocalFileOperations(
            move: (_, _) => throw new IOException("cross-volume"),
            copy: (_, _, overwrite) =>
            {
                copied = overwrite;
            },
            delete: _ => deleted = true,
            ensureDirectory: _ => { });

        ops.Move(@"D:\hot\a.jpg", @"D:\clean\a.jpg");

        Assert.True(copied);
        Assert.True(deleted);
    }

    // 防的具体回潮形态：备份写入链路（CopyAsync → AtomicCopyAsync 的 File.Replace）在加
    // Flush(flushToDisk: true) 落盘步之后退化——替换后的备份内容不完整、临时副本 .tmp.* 残留
    // 在备份目录里（.tmp 残留曾出现在 #114 handoff 快照差集里，会造成下次拆卸误捞）。
    // fsync 本身设备级不可观测，这里钉可观测的外部行为：副本内容完整 + 无临时残留 + 覆盖语义。
    // 关联 issue #37。
    [Fact]
    public async Task AtomicCopyAsync_Should_Produce_Complete_Copy_Without_Temp_Leftover()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-lfo-t37-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.jpg");
            var destination = Path.Combine(root, "backup.jpg");
            var payload = new byte[128 * 1024];
            Random.Shared.NextBytes(payload);
            await File.WriteAllBytesAsync(source, payload);

            var ops = new LocalFileOperations();

            // 不存在 → 直接落位。
            await ops.AtomicCopyAsync(source, destination, overwrite: false, CancellationToken.None);
            Assert.Equal(await File.ReadAllBytesAsync(source), await File.ReadAllBytesAsync(destination));

            // 已存在 + overwrite → 原子替换为新内容。
            await File.WriteAllTextAsync(source, "changed");
            await ops.AtomicCopyAsync(source, destination, overwrite: true, CancellationToken.None);
            Assert.Equal("changed", await File.ReadAllTextAsync(destination));

            // 临时副本不残留。
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp.*"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
