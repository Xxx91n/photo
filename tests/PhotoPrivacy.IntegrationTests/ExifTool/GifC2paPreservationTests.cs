using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// issue #8：GIF 以「部分清理」进名单的诚实性锁定（变更探测器）。
/// 防的是：ExifTool 13.61 里 GIF 的 C2PA（JUMBF 应用扩展）删不掉——官方支持表标
/// R/D，源码 GIF.pm 的 C2PA_GIF/ 条目没有 Writable、写入走 $justCopy，实测
/// -jumbf:all= 是 0 updated 静默不动。本用例钉住两件事：我们的命令删得掉该删的
/// （Comment），同时原样保留 C2PA。若 upstream 改了行为或有人往命令里塞
/// -jumbf:all=，本用例失败，届时同步更新 README「部分清理」文案与研究文档 §3.3。
/// 复审期：ExifTool 版本变更（scripts/publish.sh 钉的版本）时必须重跑。
/// 注意：Category=ExifTool 被 ci.yml/release.yml 默认过滤，人工跑：
/// dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"。
/// </summary>
[Trait("Category", "ExifTool")]
public sealed class GifC2paPreservationTests : IntegrationTestBase
{
    private const string CommentMarker = "privacy-comment-marker";

    /// <summary>最小 1x1 GIF89a：GCT + 可删的 Comment 扩展 + C2PA(JUMBF) 应用扩展。</summary>
    private static byte[] BuildGifWithC2pa()
    {
        var data = new List<byte>();
        void U16(ushort v)
        {
            data.Add((byte)(v & 0xFF));
            data.Add((byte)(v >> 8));
        }

        data.AddRange("GIF89a"u8);
        U16(1);
        U16(1);
        data.AddRange((byte[])[0x80, 0x00, 0x00]);       // GCT 存在，2 色
        data.AddRange((byte[])[0, 0, 0, 255, 255, 255]); // 黑 / 白

        // Comment 扩展（可删组，证明命令真的在干活）。
        var comment = "privacy-comment-marker"u8.ToArray();
        data.AddRange((byte[])[0x21, 0xFE, (byte)comment.Length]);
        data.AddRange(comment);
        data.Add(0x00);

        // C2PA_GIF 应用扩展：标识 8 字节 + 认证码 3 字节（0x01 0x00 0x00）；
        // GIF.pm 按「标识 + '/' + 认证码去控制符」拼查找键 → C2PA_GIF/。
        data.AddRange((byte[])[0x21, 0xFF, 0x0B]);
        data.AddRange("C2PA_GIF"u8);
        data.AddRange((byte[])[0x01, 0x00, 0x00]);

        // 合法 JUMBF：jumb 超盒（size 33）套 jumd 描述盒（size 25），validate 才干净。
        var jumd = new List<byte> { 0, 0, 0, 25 };
        jumd.AddRange("jumd"u8);
        jumd.AddRange(new byte[16]);
        jumd.Add(0x00);
        var superbox = new List<byte>();
        var superSize = 8 + jumd.Count;
        superbox.AddRange((byte[])[(byte)(superSize >> 24), (byte)(superSize >> 16), (byte)(superSize >> 8), (byte)superSize]);
        superbox.AddRange("jumb"u8);
        superbox.AddRange(jumd);
        data.Add((byte)superbox.Count);
        data.AddRange(superbox);
        data.Add(0x00);

        data.Add(0x2C);
        U16(0);
        U16(0);
        U16(1);
        U16(1);
        data.Add(0x00);
        data.AddRange((byte[])[0x02, 0x02, 0x44, 0x01, 0x00]);
        data.Add(0x3B);
        return data.ToArray();
    }

    [Fact]
    public async Task WipeMetadataAsync_Gif_Deletes_Comment_And_Preserves_C2pa()
    {
        var exifToolPath = RequireExifTool();

        var config = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                Path = exifToolPath
            }
        };

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var bridge = new ExifToolBridge(new ProcessExifToolProcess(), config, loggerFactory.CreateLogger<ExifToolBridge>());

        string? workDir = null;
        try
        {
            workDir = RealExifToolFixture.CreateWorkDirectory("gifc2pa");
            var target = Path.Combine(workDir, "c2pa.gif");
            await File.WriteAllBytesAsync(target, BuildGifWithC2pa());

            // 前置自检：C2PA 扩展和 Comment 都真的落盘了，否则后面判定失去意义。
            var before = await RealExifToolFixture.RunOneShotAsync(exifToolPath, new[] { "-v3", target });
            Assert.Contains("Application Extension: C2PA_GIF/", before.Stdout, StringComparison.Ordinal);
            Assert.Contains(CommentMarker, await ReadCommentAsync(exifToolPath, target), StringComparison.Ordinal);

            await bridge.StartAsync(CancellationToken.None);
            var result = await bridge.WipeMetadataAsync(target, CancellationToken.None);
            Assert.Equal(WipeResult.Cleaned_Modified, result);

            // 部分清理的最小承诺：该删的（Comment）删掉。
            var commentAfter = await ReadCommentAsync(exifToolPath, target);
            Assert.True(string.IsNullOrWhiteSpace(commentAfter), $"Comment 没删掉：{commentAfter}");

            // 诚实性：C2PA（JUMBF 应用扩展）原样保留，不谎报已删。
            var after = await RealExifToolFixture.RunOneShotAsync(exifToolPath, new[] { "-v3", target });
            Assert.Contains("Application Extension: C2PA_GIF/", after.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                await bridge.StopAsync(CancellationToken.None);
            }
            finally
            {
                if (workDir is not null)
                {
                    RealExifToolFixture.TryDelete(workDir);
                }
            }
        }
    }

    private static async Task<string> ReadCommentAsync(string exifToolPath, string target)
    {
        var read = await RealExifToolFixture.RunOneShotAsync(exifToolPath, new[] { "-s3", "-Comment", target });
        return read.Stdout.Trim();
    }
}
