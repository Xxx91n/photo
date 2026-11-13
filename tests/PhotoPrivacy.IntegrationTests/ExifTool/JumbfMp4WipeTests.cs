using System.Security.Cryptography;
using System.Text;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// issue #298（#290 第 3 项拆出）：MP4 的 C2PA/JUMBF 元数据删除锁定（变更探测器）。
/// 防的是：研究文档 §12.2 里「JUMBF 24→0、CBOR 50→0、mdat 载荷不变」这些实测数字只能靠
/// 样本哈希和命令手工复现，ExifTool 升级把 C2PA 删除改坏（或改好）时不会自动提醒——
/// 本测试把它钉进 CI（exiftool-stayopen job，钉扎 13.61 双平台真跑）。
/// 注意方向和 <see cref="FragmentedMp4WipeTests"/> 相反：分片 MP4 是「拒写且字节不变」，
/// 本测试是「真删」——JUMBF/CBOR 归零、mdat 载荷字节不变、文件仍可解析。
/// 也别把 GIF 那边 GifC2paPreservationTests 盯着的「C2PA 删不掉」套过来：两个容器引擎
/// 不一样，§12.2 用官方真样本实测过 MP4 的 C2PA 能删。
///
/// 样本：测试内程序化构造的最小 MP4（ftyp + 顶层 uuid[C2PA manifest] + mdat，ISO 14496-12
/// 盒结构），仓内自足、无外部下载。JUMBF 挂在顶层 uuid 盒里（ExifTool QuickTime.pm 对
/// C2PA manifest UUID 的识别条件：UUID + 4 字节 0 + "manifest\0"），uuid 载荷里是
/// jumb 超级盒（jumd 描述盒 + cbor 盒）。构造算法已用本机 ExifTool 13.61 预验
/// （读回 JUMBF 3 行 / CBOR 1 行，stay_open 生产同款块擦除 1 image files updated，
/// 擦后两组归零、mdat md5 前后一致）；CI 钉扎 13.61，行为以 CI 为准。
/// 前置自检双重把关（#82：构造失败 Assert.Fail，不静默 skip）：① 自解析盒布局
/// （ftyp 打头、uuid 载荷带 C2PA UUID 与 manifest 标记、jumb → jumd + cbor）；
/// ② ExifTool 真读回 FileType=MP4、JUMBF 标签数大于 0、CBOR DocumentID 命中标记。
///
/// 擦除走生产同款通道：BuildStartArguments（-stay_open True -@ - -q -q）+
/// BuildWipeTaskBlock（规则缺省 → WipeRuleEngine Video 族 fallback：-All= + -Time:All=），
/// stay_open 真进程擦除，断言原始输出与文件字节。
/// 注意：Category=ExifTool 被 ci.yml/release.yml 默认过滤，人工跑：
/// dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"。
/// </summary>
[Trait("Category", "ExifTool")]
public sealed class JumbfMp4WipeTests : IntegrationTestBase
{
    /// <summary>写进 CBOR documentID 的标记，证明 CBOR 载荷真的被 ExifTool 读到了。</summary>
    private const string DocumentIdMarker = "test-doc-298";

    /// <summary>C2PA manifest 盒的 UUID（c2pa.org 规范；ExifTool QuickTime.pm 靠它识别 JUMBF/CBOR）。</summary>
    private static readonly byte[] C2paManifestUuid =
    {
        0xD8, 0xFE, 0xC3, 0xD6, 0x1B, 0x0E, 0x48, 0x3C, 0x92, 0x97, 0x58, 0x28, 0x87, 0x7E, 0xC4, 0x81
    };

    /// <summary>stay_open 擦除往返的总预算（FragmentedMp4WipeTests 同款）。</summary>
    private static readonly TimeSpan TestBudget = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task WipeTaskBlock_JumbfMp4_ExifTool_Deletes_Jumbf_Cbor_And_Keeps_Mdat()
    {
        var (exe, earlyExit) = await TryLocateRealExifToolAsync();
        if (earlyExit)
        {
            return;
        }

        var config = AppConfig.Default with
        {
            ExifTool = AppConfig.Default.ExifTool with
            {
                Path = exe
            }
        };

        string? workDir = null;
        var process = new ProcessExifToolProcess();
        try
        {
            workDir = RealExifToolFixture.CreateWorkDirectory("jumbf298");
            var target = Path.Combine(workDir, "c2pa.mp4");
            await File.WriteAllBytesAsync(target, BuildJumbfMp4());

            // 前置自检 ①：自解析盒布局——ftyp 打头、顶层 uuid 盒带 C2PA manifest 结构、
            // jumb → jumd + cbor。样本构造坏掉在这里先红，后面的删除判定不失去基准。
            AssertJumbfLayout(await File.ReadAllBytesAsync(target));

            // 前置自检 ②：ExifTool 真读回——FileType=MP4、JUMBF 标签数大于 0（样本有效）、
            // CBOR DocumentID 命中标记。样本无效时测试失败而不是静默跳过（#82）。
            Assert.Equal("MP4", await RealExifToolFixture.ReadTextTagAsync(exe, target, "-FileType"));
            var jumbfBefore = await CountTagsAsync(exe, target, "-jumbf:all");
            Assert.True(jumbfBefore > 0, $"擦除前 JUMBF 标签数为 {jumbfBefore}，样本构造失效，删除判定失去基准。");
            var cborBefore = await CountTagsAsync(exe, target, "-cbor:all");
            Assert.True(cborBefore > 0, $"擦除前 CBOR 标签数为 {cborBefore}，样本构造失效，删除判定失去基准。");
            Assert.Equal(DocumentIdMarker, await RealExifToolFixture.ReadTextTagAsync(exe, target, "-cbor:DocumentID"));

            var before = await File.ReadAllBytesAsync(target);
            var mdatMd5Before = MdatPayloadMd5(before);

            // 生产同款启动参数（-stay_open True -@ - -q -q）+ 生产同款擦除块生成器；
            // 规则字典缺省走 WipeRuleEngine Video 族 fallback，本条断言顺便钉住参数面。
            var wipeBlock = ExifToolCommandBuilder.BuildWipeTaskBlock(target, taskId: "298");
            Assert.Contains("-All=", wipeBlock, StringComparison.Ordinal);
            Assert.Contains("-Time:All=", wipeBlock, StringComparison.Ordinal);

            var stdoutLines = new List<string>();
            var readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.StdoutLine += line =>
            {
                lock (stdoutLines)
                {
                    stdoutLines.Add(line);
                }
                if (line.StartsWith("{ready", StringComparison.Ordinal))
                {
                    readyTcs.TrySetResult();
                }
            };
            // 进程在 {ready} 之前就没了（坏参数/崩溃）时快速失败，不等总预算超时。
            process.ProcessExited += () => readyTcs.TrySetException(
                new InvalidOperationException("#298：ExifTool 进程在擦除块完成前退出。"));

            using var cts = new CancellationTokenSource(TestBudget);
            await process.StartAsync(exe, ExifToolCommandBuilder.BuildStartArguments(config), cts.Token);
            await process.WriteStdinAsync(wipeBlock, cts.Token);
            await readyTcs.Task.WaitAsync(cts.Token);

            // 断言 1：ExifTool 真的写了——TASK_DONE 回显 + summary 行 1 image files updated。
            // （分片 MP4 那边是 0 image files updated + 拒写报错；本测试方向相反，删成功。）
            string[] captured;
            lock (stdoutLines)
            {
                captured = stdoutLines.ToArray();
            }
            Assert.Contains(captured, l => l.Trim() == "TASK_DONE_298");
            Assert.Contains(captured, l => l.Trim() == "1 image files updated");

            var after = await File.ReadAllBytesAsync(target);

            // 断言 2：JUMBF 标签归零（brief：-JUMBF:all= 的效果由 -All= 覆盖，走生产参数面）。
            Assert.Equal(0, await CountTagsAsync(exe, target, "-jumbf:all"));

            // 断言 3：CBOR 标签随 JUMBF 一并归零。
            Assert.Equal(0, await CountTagsAsync(exe, target, "-cbor:all"));

            // 断言 4：画面数据不变——mdat 载荷 md5 前后一致（§12.2 真样本同款验证方式）。
            Assert.Equal(mdatMd5Before, MdatPayloadMd5(after));

            // 断言 5：文件仍可被 ExifTool 正常解析（FileType=MP4）。
            Assert.Equal("MP4", await RealExifToolFixture.ReadTextTagAsync(exe, target, "-FileType"));

            // 断言 6（#82 纪律的反面钉住）：不存在「测试空转绿」的路径——样本构造失败会在
            // 前置自检红、擦除没跑通会在 summary 行红、标签没删净会在计数红、画面动了会在 md5 红。
        }
        finally
        {
            try
            {
                await process.StopAsync(CancellationToken.None);
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

    /// <summary>
    /// 定位并验证真 ExifTool（FragmentedMp4WipeTests 同款）：一次 spawn <c>-ver</c> 探活。
    /// 文件不存在 → 早退（环境未提供，CI 由 exiftool-stayopen job 的 ver 门禁保证不会发生在门禁里）；
    /// 文件存在但探活失败 → Assert.Fail（环境损坏，静默早退违背 #82「不得静默 skip」）。
    /// </summary>
    private static async Task<(string Exe, bool EarlyExit)> TryLocateRealExifToolAsync()
    {
        var candidate = ExifToolLocator.TryResolve();
        if (!File.Exists(candidate))
        {
            Console.WriteLine("SKIP: 本机无可用 ExifTool（CI exiftool-stayopen job 提供门禁环境）。");
            return (candidate, EarlyExit: true);
        }
        var probe = await RealExifToolFixture.RunOneShotAsync(candidate, new[] { "-ver" });
        if (probe.ExitCode != 0 || string.IsNullOrWhiteSpace(probe.Stdout))
        {
            Assert.Fail($"#298 ExifTool 探活失败（exit={probe.ExitCode}）：{probe.Stderr}");
        }
        return (candidate, EarlyExit: false);
    }

    /// <summary>数一组标签的行数（-s3 每个标签一行）；读取失败按异常上抛，不做静默回退。</summary>
    private static async Task<int> CountTagsAsync(string exe, string targetPath, string groupArgs)
    {
        var run = await RealExifToolFixture.RunOneShotAsync(exe, new[] { "-s3", groupArgs, targetPath });
        if (run.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"读取 {groupArgs} 失败（exit={run.ExitCode}）：{run.Stderr}");
        }
        return run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    /// <summary>mdat 载荷的 md5（十六进制小写）——画面数据不变性的判定基准。</summary>
    private static string MdatPayloadMd5(byte[] data)
    {
        var mdat = ParseBoxes(data).Single(b => b.Type == "mdat");
        var payload = data[(mdat.Offset + 8)..(mdat.Offset + mdat.Length)];
        return Convert.ToHexString(MD5.HashData(payload)).ToLowerInvariant();
    }

    /// <summary>
    /// 构造带 C2PA/JUMBF 的最小 MP4（ISO 14496-12 盒结构）：ftyp + 顶层 uuid（C2PA manifest）+ mdat。
    /// JUMBF 挂顶层 uuid 盒（ExifTool QuickTime.pm 的识别条件：C2PA UUID + 4 字节 0 +
    /// "manifest\0"，之后 37 字节起按 JUMBF 盒解析）；uuid 载荷 = UUID + 0 + "manifest\0" +
    /// 8 字节 merkle 偏移占位 + jumb（jumd 描述盒 + cbor 盒）。
    /// 本构造与真实编码器无关（ExifTool 只解析盒结构、不解码视频），字节确定、无随机数。
    /// </summary>
    private static byte[] BuildJumbfMp4()
    {
        var ftyp = Box("ftyp",
            Fourcc("isom"), U32(0), Fourcc("isom"), Fourcc("iso2"), Fourcc("mp41"));

        // jumd 描述盒：16 字节类型（json + UUID 尾巴）+ toggles 0x02（带 label）+ label "c2pa\0"。
        var jumd = Box("jumd",
            Concat(
                Fourcc("json"),
                new byte[] { 0x00, 0x11, 0x00, 0x10, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 }),
            new byte[] { 0x02 },
            Ascii("c2pa\0"));

        // CBOR map { "documentID": DocumentIdMarker }：
        // 0xA1（1 对）+ 0x6A（text(10)）+ "documentID" + 0x6C（text(12)）+ 值。
        var cbor = Box("cbor", Concat(
            new byte[] { 0xA1, 0x6A }, Ascii("documentID"),
            new byte[] { 0x6C }, Ascii(DocumentIdMarker)));

        var jumb = Box("jumb", jumd, cbor);

        // 顶层 uuid 盒：C2PA manifest UUID + 4 字节 0 + "manifest\0" + 8 字节 merkle 偏移占位 + jumb。
        var uuid = Box("uuid", Concat(
            C2paManifestUuid, U32(0), Ascii("manifest\0"), BufferAlloc(8), jumb));

        var mdat = Box("mdat", BufferFilled(MdatPayloadLength, 0x33));

        return Concat(ftyp, uuid, mdat);
    }

    private const int MdatPayloadLength = 16;

    /// <summary>
    /// 自解析盒布局：顶层恰好 ftyp + uuid + mdat；uuid 载荷以 C2PA UUID 打头、带 "manifest\0"；
    /// 37 字节头之后是 jumb 超级盒，其子盒恰好 jumd + cbor。
    /// </summary>
    private static void AssertJumbfLayout(byte[] data)
    {
        var boxes = ParseBoxes(data);
        Assert.Equal(new[] { "ftyp", "uuid", "mdat" }, boxes.Select(b => b.Type).ToArray());

        var uuid = boxes[1];
        var uuidPayload = data[(uuid.Offset + 8)..(uuid.Offset + uuid.Length)];
        Assert.True(uuidPayload.Take(C2paManifestUuid.Length).SequenceEqual(C2paManifestUuid),
            "uuid 盒载荷不以 C2PA manifest UUID 打头，样本构造失效");
        Assert.True(FindFourcc(uuidPayload, Ascii("manifest\0")),
            "uuid 盒载荷里找不到 manifest 标记，样本构造失效");

        // ExifTool 从 uuid 载荷第 37 字节起按 JUMBF 盒解析（UUID 16 + 0×4 + "manifest\0" 9 + 偏移 8）。
        var jumbChildren = ParseBoxes(uuidPayload[37..]);
        Assert.Equal(new[] { "jumb" }, jumbChildren.Select(b => b.Type).ToArray());
        var jumbPayload = uuidPayload[(37 + 8)..];
        Assert.Equal(new[] { "jumd", "cbor" }, ParseBoxes(jumbPayload).Select(b => b.Type).ToArray());
    }

    /// <summary>解析一段完整盒序列（顶层或某个盒的载荷）；尺寸必须自洽且恰好铺满。</summary>
    private static List<(string Type, int Offset, int Length)> ParseBoxes(byte[] data)
    {
        var boxes = new List<(string, int, int)>();
        var pos = 0;
        while (pos + 8 <= data.Length)
        {
            var length = (int)ReadU32(data, pos);
            Assert.True(length >= 8 && pos + length <= data.Length,
                $"盒结构越界：pos={pos}, length={length}, file={data.Length}");
            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            boxes.Add((type, pos, length));
            pos += length;
        }
        Assert.Equal(data.Length, pos);
        return boxes;
    }

    private static bool FindFourcc(byte[] data, byte[] fourcc)
    {
        for (var i = 0; i + fourcc.Length <= data.Length; i++)
        {
            var match = true;
            for (var j = 0; j < fourcc.Length; j++)
            {
                if (data[i + j] != fourcc[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return true;
            }
        }
        return false;
    }

    // --- 盒结构字节工具（FragmentedMp4WipeTests 同款） ---
    private static byte[] Box(string type, params byte[][] payloads) => Box(Ascii(type), payloads);

    private static byte[] Box(byte[] type, params byte[][] payloads)
    {
        var bodyLength = payloads.Sum(p => p.Length);
        var header = new byte[8];
        WriteU32(header, 0, (uint)(bodyLength + 8));
        type.CopyTo(header, 4);
        return Concat(header, payloads);
    }

    private static byte[] Concat(byte[] first, byte[][] rest)
    {
        var total = first.Length + rest.Sum(p => p.Length);
        var result = new byte[total];
        first.CopyTo(result, 0);
        var offset = first.Length;
        foreach (var part in rest)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Fourcc(string text) => Ascii(text);

    private static byte[] U32(uint value)
    {
        var b = new byte[4];
        WriteU32(b, 0, value);
        return b;
    }

    private static void WriteU32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint ReadU32(byte[] data, int offset)
        => ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static byte[] BufferAlloc(int length) => new byte[length];

    private static byte[] BufferFilled(int length, byte value)
    {
        var b = new byte[length];
        Array.Fill(b, value);
        return b;
    }
}
