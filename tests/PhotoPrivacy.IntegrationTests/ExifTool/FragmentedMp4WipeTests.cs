using System.Text;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

/// <summary>
/// issue #292（#290 第 3 项拆出）：分片 MP4（带 moof）的擦除行为锁定（变更探测器）。
/// 防的是：仓库里没有任何测试覆盖「ExifTool 写不了分片 MP4」这条分支——ExifTool 对
/// 带 moof 的文件报 <c>Can't yet handle movie fragments when writing</c>、
/// <c>0 image files updated</c>、文件字节级不变。此前只能靠样本哈希和命令手工复现；
/// ExifTool 升级或行为变化时不会自动提醒，新测试把它钉进 CI（exiftool-stayopen job，
/// 钉扎 13.61 双平台真跑）。
///
/// 为什么不走 <see cref="ExifToolBridge.WipeMetadataAsync"/> 的返回值：桥对擦除块的
/// stdout 不收输出、无论 ExifTool 是否真写成功都返回 Cleaned_Modified——把分片 MP4
/// 误记成清理成功是 #288 的修复范围，不在本票内；本测试按 brief 断言协议层的真相：
/// 用真进程走生产同款 stay_open 启动参数（<see cref="ExifToolCommandBuilder.BuildStartArguments"/>），
/// 擦除块由 <see cref="ExifToolCommandBuilder.BuildWipeTaskBlock"/> 生成（规则缺省 →
/// WipeRuleEngine Video 族 fallback：-All= + -Time:All=），断言原始输出与文件字节。
///
/// 样本：测试内程序化构造的最小合法分片 MP4（ftyp + moov[mvhd/trak/udta-ilst 标题]
/// + 两个 moof/mdat 分片，ISO 14496-12 盒结构），仓内自足、无外部下载、无 ffmpeg。
/// 前置自检双重把关（#82：构造失败 Assert.Fail，不静默 skip）：① 自解析顶层盒结构
/// （ftyp 打头、moov 在前、两个 moof/mdat、尺寸自洽）；② ExifTool 真读回 FileType=MP4
/// 且 Title 标记落盘——样本构造坏掉或被改动都会在这里抓住，后面的拒写判定不失去基准。
/// 构造算法与行为已用本机 ExifTool 13.60 预验（拒写、字节不变）；CI 钉扎 13.61，行为以 CI 为准。
/// 注意：Category=ExifTool 被 ci.yml/release.yml 默认过滤，人工跑：
/// dotnet test PhotoPrivacy.sln --filter "Category=ExifTool"。
/// </summary>
[Trait("Category", "ExifTool")]
public sealed class FragmentedMp4WipeTests : IntegrationTestBase
{
    private const string TitleMarker = "PrivacyProbe292";

    /// <summary>stay_open 擦除往返的总预算（StayOpenConcurrentBlockTests 同款）。</summary>
    private static readonly TimeSpan TestBudget = TimeSpan.FromMinutes(5);

    /// <summary>{ready} 到达后等 stderr 报错行的宽限——报错与 {ready} 几乎同到，但不假设严格时序。</summary>
    private static readonly TimeSpan StderrGrace = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task WipeTaskBlock_FragmentedMp4_ExifTool_Refuses_Write_And_File_Unchanged()
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
            workDir = RealExifToolFixture.CreateWorkDirectory("fragmp4292");
            var target = Path.Combine(workDir, "fragmented.mp4");
            await File.WriteAllBytesAsync(target, BuildFragmentedMp4());

            // 前置自检 ①：自解析顶层盒结构——ftyp 打头、moov 在分片之前、恰好两个 moof/mdat。
            AssertFragmentedLayout(await File.ReadAllBytesAsync(target));

            // 前置自检 ②：ExifTool 真读——FileType=MP4 且 Title 标记真的落盘，
            // 否则「拒写」就可能是「文件根本不认识」的另一种失败，判定失去基准。
            Assert.Equal("MP4", await RealExifToolFixture.ReadTextTagAsync(exe, target, "-FileType"));
            Assert.Equal(TitleMarker, await RealExifToolFixture.ReadTextTagAsync(exe, target, "-QuickTime:Title"));

            var before = await File.ReadAllBytesAsync(target);

            // 生产同款启动参数（-stay_open True -@ - -q -q）+ 生产同款擦除块生成器；
            // 规则字典缺省走 WipeRuleEngine Video 族 fallback，本条断言顺便钉住参数面。
            var wipeBlock = ExifToolCommandBuilder.BuildWipeTaskBlock(target, taskId: "292");
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
                new InvalidOperationException("#292：ExifTool 进程在擦除块完成前退出。"));

            using var cts = new CancellationTokenSource(TestBudget);
            await process.StartAsync(exe, ExifToolCommandBuilder.BuildStartArguments(config), cts.Token);
            await process.WriteStdinAsync(wipeBlock, cts.Token);
            await readyTcs.Task.WaitAsync(cts.Token);

            // 断言 1：ExifTool 拒绝写入——stay_open 会话的 summary 行（-q -q 不吞）+ stderr 报错。
            string[] captured;
            lock (stdoutLines)
            {
                captured = stdoutLines.ToArray();
            }

            Assert.Contains(captured, l => l.Trim() == "0 image files updated");

            // 报错走 stderr（进程层只留最后一行）；{ready} 与报错几乎同到，给个宽限轮询，
            // 轮询超时按拿到的实际内容断言失败，不留静默通道。
            string? lastStderr = null;
            var deadline = DateTime.UtcNow + StderrGrace;
            while (DateTime.UtcNow < deadline)
            {
                lastStderr = process.LastStderrLine;
                if (lastStderr is not null && lastStderr.Contains("Can't yet handle movie fragments when writing", StringComparison.Ordinal))
                {
                    break;
                }

                await Task.Delay(50, cts.Token);
            }

            Assert.NotNull(lastStderr);
            Assert.Contains("Can't yet handle movie fragments when writing", lastStderr, StringComparison.Ordinal);

            // 断言 2：文件字节级不变——整个文件前后逐字节比对（brief：md5 或逐盒字节等价，
            // 全量字节比对是两者中最严的）。
            var after = await File.ReadAllBytesAsync(target);
            Assert.Equal(before, after);

            // 断言 3（#82 纪律的反面钉住）：以上任何一环没走通都已在前面红掉——
            // 进程起来过（{ready} 到了）、输出抓到了、报错对上了、字节没动。
            // 不存在「测试空转绿」的路径：样本构造失败会抛、ExifTool 损坏会在前置自检红。
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
    /// 定位并验证真 ExifTool（StayOpenConcurrentBlockTests 先例）：一次 spawn <c>-ver</c> 探活。
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
            Assert.Fail($"#292 ExifTool 探活失败（exit={probe.ExitCode}）：{probe.Stderr}");
        }

        return (candidate, EarlyExit: false);
    }

    /// <summary>
    /// 构造最小合法分片 MP4（ISO 14496-12 盒结构）：ftyp + moov（mvhd / trak / udta-meta-ilst
    /// 带 iTunes 式 ©nam 标题）+ 两个 moof/mdat 分片。moof 盒的存在即 ExifTool 拒写的触发条件。
    /// 本构造与真实编码器无关（ExifTool 只解析盒结构、不解码视频），字节确定、无随机数。
    /// </summary>
    private static byte[] BuildFragmentedMp4() => BuildFragmentedMp4(mdat1ChunkOffset: null);

    private static byte[] BuildFragmentedMp4(uint? mdat1ChunkOffset)
    {
        var ftyp = Box("ftyp",
            Fourcc("isom"),
            U32(512),
            Fourcc("isom"), Fourcc("iso2"), Fourcc("avc1"), Fourcc("mp41"));

        // 36 字节 unity 矩阵（扁平字节数组；node 预验版同款——写错维度会连 moov 都拼不出来）
        var matrix = Concat(
            U32(0x00010000), U32(0), U32(0),
            U32(0), U32(0x00010000), U32(0),
            U32(0), U32(0), U32(0x40000000));

        var mvhd = Box("mvhd",
            U32(0),              // version 0 + flags
            U32(0), U32(0),      // creation / modification
            U32(1000),           // timescale
            U32(2048),           // duration（2 片 × 1024）
            U32(0x00010000),     // rate 1.0
            U16(0x0100),         // volume
            U16(0), BufferAlloc(8),
            matrix,
            BufferAlloc(24),     // pre_defined：bit(32)[6]，ISO 14496-12 §8.2.2.2（本机 13.60 复验过读回与拒写行为）
            U32(2));             // next_track_ID

        var tkhd = Box("tkhd",
            U32(7),              // flags: enabled | in_movie | in_preview
            U32(0), U32(0),
            U32(1),              // track_ID
            U32(0),
            U32(2048),           // duration
            BufferAlloc(8),
            U16(0), U16(0),      // layer / alternate_group
            U16(0),              // volume：视频轨按规格为 0（本机 13.60 复验过读回与拒写行为）
            U16(0),
            matrix,
            U32(0x00010000), U32(0x00010000)); // width / height (16.16)

        var mdhd = Box("mdhd",
            U32(0), U32(0), U32(0),
            U32(1000), U32(2048),
            U16(0x55c4),         // language 'und'
            U16(0));

        var hdlrVide = Box("hdlr", U32(0), U32(0), Fourcc("vide"), BufferAlloc(12), Ascii("video\0"));

        var vmhd = Box("vmhd", U32(1), U16(0), U16(0), U16(0), U16(0)); // graphicsmode + opcolor[3]，ISO 14496-12 §8.5.2.2
        var dref = Box("dref", U32(0), U32(1), Box("url ", U32(1)));
        var dinf = Box("dinf", dref);

        // 最小 mp4v 样本条目（无 esds：ExifTool 只解析盒结构）
        var mp4vEntry = new[]
        {
            BufferAlloc(6),      // reserved
            U16(1),              // data_reference_index
            U16(0), U16(0),
            BufferAlloc(12),
            U16(2), U16(2),      // width / height
            U32(0x00480000), U32(0x00480000), // 72 dpi
            U32(0),
            U16(1),              // frame_count
            BufferAlloc(32),     // compressorname
            U16(0x0018),         // depth
            U16(0xffff),
        };
        var stsd = Box("stsd", U32(0), U32(1), Box("mp4v", mp4vEntry));
        var stts = Box("stts", U32(0), U32(0));
        var stsc = Box("stsc", U32(0), U32(0));
        var stsz = Box("stsz", U32(0), U32(0), U32(1), U32(MdatPayloadLength));
        // 第一遍构造时 chunk offset 占位为 0，第二遍按真实偏移重建（见方法入口两个重载）。
        var stco = Box("stco", U32(0), U32(1), U32(mdat1ChunkOffset ?? 0));
        var stbl = Box("stbl", stsd, stts, stsc, stsz, stco);
        var minf = Box("minf", vmhd, dinf, stbl);
        var mdia = Box("mdia", mdhd, hdlrVide, minf);
        var trak = Box("trak", tkhd, mdia);

        // iTunes 式标题：udta > meta(ver/flags + hdlr(mdir/appl) + ilst > ©nam > data)
        var hdlrMdir = Box("hdlr", U32(0), U32(0), Fourcc("mdir"), BufferAlloc(8), Ascii("appl\0"));
        var dataBox = Box("data", U32(1), U32(0), Ascii(TitleMarker)); // well-known type 1 = UTF-8
        // © 的字节是 0xA9——Encoding.ASCII 会把它换成 '?'，这里必须手写字节。
        var cnam = Box(new byte[] { 0xA9, (byte)'n', (byte)'a', (byte)'m' }, dataBox);
        var ilst = Box("ilst", cnam);
        var meta = Box("meta", U32(0), hdlrMdir, ilst);
        var udta = Box("udta", meta);

        var moov = Box("moov", mvhd, trak, udta);

        // 布局：ftyp | moov | moof1 | mdat1 | moof2 | mdat2
        // trun.data_offset 相对 moof 起点（tfhd default-base-is-moof）：moof 盒 + mdat 盒头 8 字节。
        // moof 盒 = 8 头 + mfhd(16) + traf(8 + tfhd(16) + tfdt(16) + trun(24)) = 88。
        const int moofBoxLength = 88;
        var mdat1PayloadStart = (uint)(ftyp.Length + moov.Length + moofBoxLength + 8);
        if (mdat1ChunkOffset is null)
        {
            // 占位 stco 与真实 stco 等长，moov 尺寸不变，第二遍重建拿到真实 chunk offset。
            return BuildFragmentedMp4(mdat1ChunkOffset: mdat1PayloadStart);
        }

        var moof1 = BuildMoof(sequenceNumber: 1, baseMediaDecodeTime: 0, dataOffset: (int)moofBoxLength + 8);
        // 自钉（R2 配套要求）：moofBoxLength 是 trun.data_offset 的手工对齐基准——
        // BuildMoof 增删字段后这里必须先红，而不是让样本悄悄变形。
        Assert.Equal(moofBoxLength, moof1.Length);
        var mdat1 = Box("mdat", BufferFilled(MdatPayloadLength, 0x11));
        var moof2 = BuildMoof(sequenceNumber: 2, baseMediaDecodeTime: 1024, dataOffset: (int)moofBoxLength + 8);
        var mdat2 = Box("mdat", BufferFilled(MdatPayloadLength, 0x22));

        return Concat(ftyp, moov, moof1, mdat1, moof2, mdat2);
    }

    private const int MdatPayloadLength = 16;

    private static byte[] BuildMoof(int sequenceNumber, uint baseMediaDecodeTime, int dataOffset) => Box("moof",
        Box("mfhd", U32(0), U32((uint)sequenceNumber)),
        // ISO 14496-12 §8.8：媒体分片的轨道信息在 traf 里，tfdt/trun 不能直接挂 moof 下。
        Box("traf",
            Box("tfhd", U32(0x020000), U32(1)), // default-base-is-moof + track_ID 1
            Box("tfdt", U32(0), U32(baseMediaDecodeTime)), // version 0：32 位 baseMediaDecodeTime
            // trun flags 0x00000201 = data-offset-present | sample-size-present；1 个样本、16 字节
            Box("trun", U32(0x00000201), U32(1), I32(dataOffset), U32(MdatPayloadLength))));

    /// <summary>解析顶层盒并断言分片布局：ftyp 打头、moov 在 moof 之前、恰好两个 moof/mdat，尺寸自洽。</summary>
    private static void AssertFragmentedLayout(byte[] data)
    {
        var boxes = ParseBoxes(data);
        Assert.True(boxes.Count >= 5, $"顶层盒数量不足：{boxes.Count}");
        Assert.Equal("ftyp", boxes[0].Type);
        Assert.Equal("moov", boxes[1].Type);

        var fragments = boxes.Skip(2).Select(b => b.Type).ToArray();
        Assert.Equal(new[] { "moof", "mdat", "moof", "mdat" }, fragments);

        // moov 里必须真有 moof 会有的东西之外的 ilst 标题盒（©nam），证明元数据真的在 moov 内。
        var moovPayload = data[(boxes[1].Offset + 8)..(boxes[1].Offset + boxes[1].Length)];
        Assert.True(FindFourcc(moovPayload, new byte[] { 0xA9, (byte)'n', (byte)'a', (byte)'m' }),
            "moov 内找不到 ©nam 标题盒，样本元数据构造失效");

        // 分片内部结构（CodeRabbit #295）：moof → traf → tfhd/tfdt/trun（ISO 14496-12 §8.8）。
        // 没有 traf/tfhd 的扁平 moof 不是合法媒体分片——顶层盒自检抓不住这种畸形，必须往下钻一层。
        var moofPayload = data[(boxes[2].Offset + 8)..(boxes[2].Offset + boxes[2].Length)];
        var moofChildren = ParseBoxes(moofPayload);
        Assert.Equal(new[] { "mfhd", "traf" }, moofChildren.Select(b => b.Type).ToArray());
        var trafPayload = moofPayload[(moofChildren[1].Offset + 8)..(moofChildren[1].Offset + moofChildren[1].Length)];
        Assert.Equal(new[] { "tfhd", "tfdt", "trun" }, ParseBoxes(trafPayload).Select(b => b.Type).ToArray());
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

    // --- 盒结构字节工具 ---

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

    private static byte[] U16(ushort value)
    {
        var b = new byte[2];
        b[0] = (byte)(value >> 8);
        b[1] = (byte)value;
        return b;
    }

    private static byte[] I32(int value)
    {
        var b = new byte[4];
        b[0] = (byte)(value >> 24);
        b[1] = (byte)(value >> 16);
        b[2] = (byte)(value >> 8);
        b[3] = (byte)value;
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
