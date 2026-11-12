using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// issue #283：PSD 以「部分清理」进名单后的真实文件锁定（变更探测器）。
/// 防的是：PSD 的 `-all= --icc_profile:all` 只重写 image resource block（IRB），
/// 本用例钉住三件事：1) IRB 里隐私资源（0x0422 EXIF、0x0424 XMP）删掉；
/// 2) 0x040F ICC 字节不变（ICC 保留承诺），0x03ED 分辨率、0x0400 版权标记、
/// 0x0408 URL 原样保留——这正是「部分清理」说明承诺保留的清单；
/// 3) IRB 之外的图层与蒙版段、图像数据段字节不变（ProcessPSD 只重写 IRB，
/// 头/color mode/图层/图像数据原样拷贝，依据 docs/research/exiftool-jxl-webp-psd-gif-apng.md §3.4）。
/// 样本来源：按 Adobe PSD 文件格式规范手工构造的 1x1 RGB/8bit PSD（分辨率 72dpi、
/// 版权标记、URL、sRGB ICC，各资源 ID 见规范），带 1 个图层的图层蒙版段 + raw 图像数据，
/// 以 base64 内嵌，无外部依赖；EXIF/XMP 由 exiftool 一次性写入（同 CreateTaggedJpegAsync 模式），
/// 写入后读回确认真落盘，擦除判定才有意义。本机用 ExifTool 13.60 预验过同款结构；
/// CI 钉扎 13.61（scripts/publish.sh），行为以 CI 为准。
/// 注意：Category=ExifTool 被 ci.yml/release.yml 默认过滤，人工跑：
/// dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"。
/// </summary>
[Trait("Category", "ExifTool")]
public sealed class PsdMetadataWipeTests : IntegrationTestBase
{
    private const string TagMarker = "PrivacyProbe283";
    private const string XmpTitleMarker = "privacy-xmp-283";

    /// <summary>Photoshop image resource block 的资源 ID（Adobe PSD 规范）。</summary>
    private const ushort ResourceResolution = 0x03ED;
    private const ushort ResourceCopyrightFlag = 0x0400;
    private const ushort ResourceUrl = 0x0408;
    private const ushort ResourceIccProfile = 0x040F;
    private const ushort ResourceExif = 0x0422;
    private const ushort ResourceXmp = 0x0424;

    /// <summary>
    /// 1x1 RGB/8bit PSD（base64）：头 + 空 color mode + IRB（分辨率/版权/URL/ICC）
    /// + 带 1 个图层的图层蒙版段 + raw 图像数据。EXIF/XMP 资源运行时由 exiftool 写入。
    /// </summary>
    private const string SamplePsdBase64 =
        "OEJQUwABAAAAAAAAAAMAAAABAAAAAQAIAAIAAAAAAAACpDhCSU0D7QEAAAAAEABIAAAAAQABAEgAAAABAAE4QklNBAABAAAA" +
        "AAEBADhCSU0ECAEAAAAAFmh0dHA6Ly9wcml2YWN5LmV4YW1wbGU4QklNBA8BAAAAAkwAAAJMbGNtcwRAAABtbnRyUkdCIFhZ" +
        "WiAH6gALAAwACwAEAB5hY3NwTVNGVAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA9tYAAQAAAADTLWxjbXMAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAtkZXNjAAABCAAAADZjcHJ0AAABQAAAAEx3dHB0AAABjAAA" +
        "ABRjaGFkAAABoAAAACxyWFlaAAABzAAAABRiWFlaAAAB4AAAABRnWFlaAAAB9AAAABRyVFJDAAACCAAAACBnVFJDAAACCAAA" +
        "ACBiVFJDAAACCAAAACBjaHJtAAACKAAAACRtbHVjAAAAAAAAAAEAAAAMZW5VUwAAABoAAAAcAHMAUgBHAEIAIABiAHUAaQBs" +
        "AHQALQBpAG4AAG1sdWMAAAAAAAAAAQAAAAxlblVTAAAAMAAAABwATgBvACAAYwBvAHAAeQByAGkAZwBoAHQALAAgAHUAcwBl" +
        "ACAAZgByAGUAZQBsAHlYWVogAAAAAAAA9tYAAQAAAADTLXNmMzIAAAAAAAEMQgAABd7///MlAAAHkwAA/ZD///uh///9ogAA" +
        "A9wAAMBuWFlaIAAAAAAAAG+gAAA49QAAA5BYWVogAAAAAAAAJJ8AAA+EAAC2w1hZWiAAAAAAAABilwAAt4cAABjZcGFyYQAA" +
        "AAAAAwAAAAJmZgAA8qcAAA1ZAAAT0AAACltjaHJtAAAAAAADAAAAAKPXAABUewAATM0AAJmaAAAmZgAAD1wAAABpAAAAYQAB" +
        "AAAAAAAAAAAAAAABAAAAAQAFAAAAAAAAAAAAAAABAAEAAAABAAIAAAABgAEAAAABOEJJTW5vcm3/AAAAAAAAEAAAAAAAAAAA" +
        "B0xheWVyIDEAAEAAAEAAAEAAAEAAAEAAAAAAAAAQIDA=";

    [Fact]
    public async Task WipeMetadataAsync_Psd_Deletes_Exif_Xmp_Preserves_Irb_Scalars_Layers_And_Image_Data()
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
            workDir = RealExifToolFixture.CreateWorkDirectory("psdwipe283");
            var target = Path.Combine(workDir, "probe.psd");
            await File.WriteAllBytesAsync(target, Convert.FromBase64String(SamplePsdBase64));

            // 前置自检：bare 样本只带四个保留类资源，EXIF/XMP 还没写进去。
            var bare = new PsdSections(await File.ReadAllBytesAsync(target));
            Assert.Equal(
                new[] { ResourceResolution, ResourceCopyrightFlag, ResourceUrl, ResourceIccProfile },
                bare.ResourceIds);

            // exiftool 一次性写入 EXIF / XMP（同 CreateTaggedJpegAsync 模式），读回确认真落盘。
            var write = await RealExifToolFixture.RunOneShotAsync(exifToolPath, new[]
            {
                "-overwrite_original", "-q", "-q",
                $"-EXIF:Make={TagMarker}",
                "-EXIF:Model=PsdFixture283",
                $"-XMP-dc:Title={XmpTitleMarker}",
                target
            });
            Assert.Equal(0, write.ExitCode);
            Assert.Equal(TagMarker, await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-EXIF:Make"));
            Assert.Equal(XmpTitleMarker, await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-XMP-dc:Title"));

            var tagged = new PsdSections(await File.ReadAllBytesAsync(target));
            Assert.Contains(ResourceExif, tagged.ResourceIds);
            Assert.Contains(ResourceXmp, tagged.ResourceIds);

            await bridge.StartAsync(CancellationToken.None);
            var result = await bridge.WipeMetadataAsync(target, CancellationToken.None);
            Assert.Equal(WipeResult.Cleaned_Modified, result);

            var wiped = new PsdSections(await File.ReadAllBytesAsync(target));

            // 隐私资源：EXIF / XMP 删掉；ExifTool 读回不到。
            Assert.DoesNotContain(ResourceExif, wiped.ResourceIds);
            Assert.DoesNotContain(ResourceXmp, wiped.ResourceIds);
            Assert.True(string.IsNullOrWhiteSpace(await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-EXIF:Make")),
                "擦除后 EXIF:Make 还能读到");
            Assert.True(string.IsNullOrWhiteSpace(await RealExifToolFixture.ReadTextTagAsync(exifToolPath, target, "-XMP-dc:Title")),
                "擦除后 XMP-dc:Title 还能读到");

            // 部分清理承诺：IRB 标量与 ICC 原样保留——四个保留类资源逐个比 payload
            // （字节级不变；CodeRabbit #285 nitpick：只查 ID 列表会漏掉内容被改写的情况）。
            Assert.Equal(tagged.GetResource(ResourceIccProfile), wiped.GetResource(ResourceIccProfile));
            Assert.Equal(tagged.GetResource(ResourceResolution), wiped.GetResource(ResourceResolution));
            Assert.Equal(tagged.GetResource(ResourceCopyrightFlag), wiped.GetResource(ResourceCopyrightFlag));
            Assert.Equal(tagged.GetResource(ResourceUrl), wiped.GetResource(ResourceUrl));
            Assert.Equal(
                new[] { ResourceResolution, ResourceCopyrightFlag, ResourceUrl, ResourceIccProfile },
                wiped.ResourceIds);

            // 内容不变承诺：图层与蒙版段、图像数据段字节不变（ProcessPSD 只重写 IRB）。
            Assert.Equal(tagged.LayerAndMaskSection, wiped.LayerAndMaskSection);
            Assert.Equal(tagged.ImageDataSection, wiped.ImageDataSection);
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

    /// <summary>PSD 段解析：IRB（8BIM 块）+ 图层蒙版段 + 图像数据段；非 PSD 或越界直接抛错。</summary>
    private sealed class PsdSections
    {
        private const int HeaderSize = 26;

        public PsdSections(byte[] data)
        {
            Assert.True(data.Length > HeaderSize + 8, "文件短于 PSD 头");
            Assert.Equal("8BPS"u8.ToArray(), data[..4]);

            var pos = HeaderSize;
            var colorModeLength = BinaryPrimitives.ReadInt32BigEndian(data[pos..]); // color mode data 长度
            pos += 4 + colorModeLength;

            var resourceLength = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            pos += 4;
            var resourceEnd = pos + resourceLength;
            Resources = ParseImageResources(data, pos, resourceEnd);
            pos = resourceEnd;

            var layerMaskLength = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
            pos += 4;
            LayerAndMaskSection = data[pos..(pos + layerMaskLength)];
            pos += layerMaskLength;
            ImageDataSection = data[pos..];
        }

        /// <summary>资源 ID → payload（奇数长度后带填充字节）。</summary>
        public Dictionary<ushort, byte[]> Resources { get; }

        public byte[] LayerAndMaskSection { get; }
        public byte[] ImageDataSection { get; }

        public ushort[] ResourceIds => Resources.Keys.Order().ToArray();

        public byte[]? GetResource(ushort id) => Resources.TryGetValue(id, out var payload) ? payload : null;

        private static Dictionary<ushort, byte[]> ParseImageResources(byte[] data, int start, int end)
        {
            var blocks = new Dictionary<ushort, byte[]>();
            var pos = start;
            while (pos + 12 <= end)
            {
                Assert.Equal("8BIM"u8.ToArray(), data[pos..(pos + 4)]);
                var id = BinaryPrimitives.ReadUInt16BigEndian(data[(pos + 4)..]);
                var nameLength = data[pos + 6];
                var namePadded = (nameLength + 1 + 1) & ~1; // Pascal 串 + 偶对齐
                pos += 6 + namePadded;
                var size = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
                pos += 4;
                Assert.True(size >= 0 && pos + size <= end, $"资源 0x{id:X4} 尺寸越界");
                blocks[id] = data[pos..(pos + size)];
                pos += size + (size & 1);
            }

            Assert.Equal(end, pos);
            return blocks;
        }
    }
}
