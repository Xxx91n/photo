using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// issue #283：WebP 以「部分清理」进名单后的真实文件锁定（变更探测器）。
/// 防的是：命令串测试只能证明 `-all= --icc_profile:all` 这个字符串存在，
/// 不能证明 ExifTool 对 WebP 容器真的删得掉 EXIF/XMP chunk、真的原样保留
/// ICCP 与图像数据。本用例用真实 ExifTool 走 stay_open 擦除，断言：
/// 1) EXIF / XMP chunk 没了；2) ICCP 字节不变（ICC 保留承诺）；3) 图像数据
/// chunk（VP8）字节不变（内容不变承诺）；4) 擦后 EXIF:Make / XMP-dc:Title 读不回。
/// VP8X 的标志位会随元数据删除而更新（EXIF/XMP 位清零），属预期，不参与比对。
/// 样本来源：Pillow 生成的 2x2 VP8 有损 WebP（sRGB ICCP + EXIF），再按
/// RIFF 容器规范注入 XMP chunk（生成方式见 docs/research/exiftool-jxl-webp-psd-gif-apng.md §3.1），
/// 以 base64 内嵌，无外部依赖。本机用 ExifTool 13.60 预验过同款结构；
/// CI 钉扎 13.61（scripts/publish.sh），行为以 CI 为准。
/// 注意：Category=ExifTool 被 ci.yml/release.yml 默认过滤，人工跑：
/// dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"。
/// </summary>
[Trait("Category", "ExifTool")]
public sealed class WebpMetadataWipeTests : IntegrationTestBase
{
    private const string TagMarker = "PrivacyProbe283";
    private const string XmpTitleMarker = "privacy-xmp-283";

    /// <summary>
    /// 2x2 VP8 有损 WebP：VP8X(扩展格式) + ICCP(sRGB) + VP8(图像数据) + EXIF + XMP。
    /// 生成：Pillow `Image.new("RGB",(2,2)).save(...,"WEBP",icc_profile=…,exif=…,method=0)`
    /// 后按 RIFF 容器规范补 XMP chunk；这串字节是冻结样本，改动后前置自检会失败。
    /// </summary>
    private const string SampleWebpBase64 =
        "UklGRkYEAABXRUJQVlA4WAoAAAAoAAAAAQAAAQAASUNDUEwCAAAAAAJMbGNtcwRAAABtbnRyUkdCIFhZWiAH6gALAAwADAAy" +
        "AAxhY3NwTVNGVAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA9tYAAQAAAADTLWxjbXMAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAtkZXNjAAABCAAAADZjcHJ0AAABQAAAAEx3dHB0AAABjAAAABRjaGFkAAABoAAA" +
        "ACxyWFlaAAABzAAAABRiWFlaAAAB4AAAABRnWFlaAAAB9AAAABRyVFJDAAACCAAAACBnVFJDAAACCAAAACBiVFJDAAACCAAA" +
        "ACBjaHJtAAACKAAAACRtbHVjAAAAAAAAAAEAAAAMZW5VUwAAABoAAAAcAHMAUgBHAEIAIABiAHUAaQBsAHQALQBpAG4AAG1s" +
        "dWMAAAAAAAAAAQAAAAxlblVTAAAAMAAAABwATgBvACAAYwBvAHAAeQByAGkAZwBoAHQALAAgAHUAcwBlACAAZgByAGUAZQBs" +
        "AHlYWVogAAAAAAAA9tYAAQAAAADTLXNmMzIAAAAAAAEMQgAABd7///MlAAAHkwAA/ZD///uh///9ogAAA9wAAMBuWFlaIAAA" +
        "AAAAAG+gAAA49QAAA5BYWVogAAAAAAAAJJ8AAA+EAAC2w1hZWiAAAAAAAABilwAAt4cAABjZcGFyYQAAAAAAAwAAAAJmZgAA" +
        "8qcAAA1ZAAAT0AAACltjaHJtAAAAAAADAAAAAKPXAABUewAATM0AAJmaAAAmZgAAD1xWUDggIgAAADABAJ0BKgIAAgABQCYl" +
        "lAADcAD+/DeL22WiUD7k/RGAAABFWElGYgAAAE1NACoAAAAIAAMBDwACAAAAEAAAADIBEAACAAAADAAAAEIBOwACAAAAEwAA" +
        "AE4AAAAAUHJpdmFjeVByb2JlMjgzAFdlYnBGaXh0dXJlAHByaXZhY3ktYXJ0aXN0LTI4MwAAWE1QIEABAAA8P3hwYWNrZXQg" +
        "YmVnaW49IiIgaWQ9Ilc1TTBNcENlaGlIenJlU3pOVGN6a2M5ZCI/Pjx4OnhtcG1ldGEgeG1sbnM6eD0iYWRvYmU6bnM6bWV0" +
        "YS8iPjxyZGY6UkRGIHhtbG5zOnJkZj0iaHR0cDovL3d3dy53My5vcmcvMTk5OS8wMi8yMi1yZGYtc3ludGF4LW5zIyI+PHJk" +
        "ZjpEZXNjcmlwdGlvbiByZGY6YWJvdXQ9IiIgeG1sbnM6ZGM9Imh0dHA6Ly9wdXJsLm9yZy9kYy9lbGVtZW50cy8xLjEvIj48" +
        "ZGM6dGl0bGU+cHJpdmFjeS14bXAtMjgzPC9kYzp0aXRsZT48L3JkZjpEZXNjcmlwdGlvbj48L3JkZjpSREY+PC94OnhtcG1l" +
        "dGE+PD94cGFja2V0IGVuZD0idyI/Pg==";

    [Fact]
    public async Task WipeMetadataAsync_Webp_Deletes_Exif_Xmp_Preserves_Icc_And_Image_Data()
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
            workDir = RealExifToolFixture.CreateWorkDirectory("webpwipe283");
            var target = Path.Combine(workDir, "probe.webp");
            await File.WriteAllBytesAsync(target, Convert.FromBase64String(SampleWebpBase64));

            // 前置自检：EXIF / XMP 真的落盘了，且容器结构符合预期，否则后面判定失去意义。
            Assert.Equal(TagMarker, await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-EXIF:Make"));
            Assert.Equal(XmpTitleMarker, await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-XMP-dc:Title"));
            var before = ParseRiffChunks(await File.ReadAllBytesAsync(target));
            Assert.Contains("EXIF", before.Keys);
            Assert.Contains("XMP ", before.Keys);
            Assert.Contains("ICCP", before.Keys);
            Assert.Contains("VP8 ", before.Keys);

            await bridge.StartAsync(CancellationToken.None);
            var result = await bridge.WipeMetadataAsync(target, CancellationToken.None);
            Assert.Equal(WipeResult.Cleaned_Modified, result);

            var afterBytes = await File.ReadAllBytesAsync(target);
            var after = ParseRiffChunks(afterBytes);

            // 隐私字段：EXIF / XMP chunk 删掉。
            Assert.False(after.ContainsKey("EXIF"), "EXIF chunk 没删掉");
            Assert.False(after.ContainsKey("XMP "), "XMP chunk 没删掉");

            // ICC 保留承诺：ICCP chunk 原样（字节级）保留。
            Assert.True(after.TryGetValue("ICCP", out var iccAfter), "ICCP chunk 擦除后丢了");
            Assert.Equal(before["ICCP"], iccAfter);

            // 内容不变承诺：图像数据 chunk（VP8 payload）字节不变。
            Assert.True(after.TryGetValue("VP8 ", out var vp8After), "VP8 chunk 擦除后丢了");
            Assert.Equal(before["VP8 "], vp8After);

            // 双保险：擦后 ExifTool 读回不到隐私标签。
            Assert.True(string.IsNullOrWhiteSpace(await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-EXIF:Make")),
                "擦除后 EXIF:Make 还能读到");
            Assert.True(string.IsNullOrWhiteSpace(await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-XMP-dc:Title")),
                "擦除后 XMP-dc:Title 还能读到");
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

    /// <summary>解析 RIFF/WebP 容器的顶层 chunk（fourcc → payload）；非 RIFF/WEBP 直接抛错。</summary>
    private static Dictionary<string, byte[]> ParseRiffChunks(byte[] data)
    {
        Assert.True(data.Length >= 12, "文件短于 RIFF 头");
        Assert.Equal("RIFF"u8.ToArray(), data[..4]);
        Assert.Equal("WEBP"u8.ToArray(), data[8..12]);
        // CodeRabbit #285：顶层 RIFF 尺寸必须等于文件字节数 - 8，样本头部损坏时 ExifTool 会容错读取，那不算正常 WebP 的证据。
        Assert.Equal(data.Length - 8, BitConverter.ToInt32(data, 4)); // 小端

        var chunks = new Dictionary<string, byte[]>();
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            var fourcc = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            var size = BitConverter.ToInt32(data, pos + 4); // RIFF 尺寸小端
            if (size < 0 || pos + 8 + size > data.Length)
            {
                throw new InvalidOperationException($"chunk {fourcc} 尺寸越界：size={size}，pos={pos}");
            }

            chunks[fourcc] = data[(pos + 8)..(pos + 8 + size)];
            pos += 8 + size + (size & 1); // 奇数 payload 后有填充字节
        }

        return chunks;
    }
}
