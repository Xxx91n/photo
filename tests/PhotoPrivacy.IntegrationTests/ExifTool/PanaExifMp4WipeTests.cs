using System.Security.Cryptography;
using System.Text;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Core.ExifTool;

namespace PhotoPrivacy.IntegrationTests.ExifTool;

[Trait("Category", "ExifTool")]
public sealed class PanaExifMp4WipeTests : IntegrationTestBase
{
    private const string MakeMarker = "Panasonic";
    private static readonly TimeSpan TestBudget = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task WipeTaskBlock_PanaExifMp4_ExifTool_Deletes_Exif_And_Keeps_Mdat()
    {
        var (exe, earlyExit) = await TryLocateRealExifToolAsync();
        if (earlyExit) return;

        var config = AppConfig.Default with { ExifTool = AppConfig.Default.ExifTool with { Path = exe } };
        string? workDir = null;
        var process = new ProcessExifToolProcess();
        try
        {
            workDir = RealExifToolFixture.CreateWorkDirectory("pana302");
            var target = Path.Combine(workDir, "pana.mp4");
            await File.WriteAllBytesAsync(target, BuildPanaExifMp4());
            AssertPanaLayout(await File.ReadAllBytesAsync(target));
            Assert.Equal("MP4", await RealExifToolFixture.ReadTextTagAsync(exe, target, "-FileType"));
            Assert.Equal(MakeMarker, await RealExifToolFixture.ReadTextTagAsync(exe, target, "-EXIF:Make"));
            var exifBefore = await CountTagsAsync(exe, target, "-EXIF:All");
            Assert.True(exifBefore > 0, $"擦除前 EXIF 标签数为 {exifBefore}，样本构造失效，删除判定失去基准。");
            var before = await File.ReadAllBytesAsync(target);
            var mdatMd5Before = MdatPayloadMd5(before);
            var wipeBlock = ExifToolCommandBuilder.BuildWipeTaskBlock(target, taskId: "302");
            Assert.Contains("-All=", wipeBlock, StringComparison.Ordinal);
            Assert.Contains("-Time:All=", wipeBlock, StringComparison.Ordinal);
            var stdoutLines = new List<string>();
            var readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.StdoutLine += line => { lock (stdoutLines) { stdoutLines.Add(line); } if (line.StartsWith("{ready", StringComparison.Ordinal)) readyTcs.TrySetResult(); };
            process.ProcessExited += () => readyTcs.TrySetException(new InvalidOperationException("#302：ExifTool 进程在擦除块完成前退出。"));
            using var cts = new CancellationTokenSource(TestBudget);
            await process.StartAsync(exe, ExifToolCommandBuilder.BuildStartArguments(config), cts.Token);
            await process.WriteStdinAsync(wipeBlock, cts.Token);
            await readyTcs.Task.WaitAsync(cts.Token);
            string[] captured; lock (stdoutLines) { captured = stdoutLines.ToArray(); }
            Assert.Contains(captured, l => l.Trim() == "TASK_DONE_302");
            Assert.Contains(captured, l => l.Trim() == "1 image files updated");
            var after = await File.ReadAllBytesAsync(target);
            Assert.Equal(0, await CountTagsAsync(exe, target, "-EXIF:All"));
            Assert.Equal(mdatMd5Before, MdatPayloadMd5(after));
            Assert.Equal("MP4", await RealExifToolFixture.ReadTextTagAsync(exe, target, "-FileType"));
        }
        finally
        {
            try { await process.StopAsync(CancellationToken.None); }
            finally { if (workDir is not null) RealExifToolFixture.TryDelete(workDir); }
        }
    }

    private static async Task<(string Exe, bool EarlyExit)> TryLocateRealExifToolAsync()
    {
        var candidate = ExifToolLocator.TryResolve();
        if (!File.Exists(candidate)) { Console.WriteLine("SKIP: 本机无可用 ExifTool（CI exiftool-stayopen job 提供门禁环境）。"); return (candidate, EarlyExit: true); }
        var probe = await RealExifToolFixture.RunOneShotAsync(candidate, new[] { "-ver" });
        if (probe.ExitCode != 0 || string.IsNullOrWhiteSpace(probe.Stdout)) Assert.Fail($"#302 ExifTool 探活失败（exit={probe.ExitCode}）：{probe.Stderr}");
        return (candidate, EarlyExit: false);
    }

    private static async Task<int> CountTagsAsync(string exe, string targetPath, string groupArgs)
    {
        var run = await RealExifToolFixture.RunOneShotAsync(exe, new[] { "-s3", groupArgs, targetPath });
        if (run.ExitCode != 0) throw new InvalidOperationException($"读取 {groupArgs} 失败（exit={run.ExitCode}）：{run.Stderr}");
        return run.Stdout.Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private static string MdatPayloadMd5(byte[] data)
    {
        var mdat = ParseBoxes(data).Single(b => b.Type == "mdat");
        var payload = data[(mdat.Offset + 8)..(mdat.Offset + mdat.Length)];
        return Convert.ToHexString(MD5.HashData(payload)).ToLowerInvariant();
    }

    private static byte[] BuildPanaExifMp4()
    {
        var ftyp = Box("ftyp", Fourcc("isom"), U32(0), Fourcc("isom"), Fourcc("iso2"), Fourcc("mp41"));
        var makeField = Concat(Ascii(MakeMarker), BufferAlloc(16 - MakeMarker.Length));
        var panaData = Concat(makeField, BuildExifPayload());
        var pana = Box("PANA", panaData);
        var udta = Box("udta", pana);
        var moov = Box("moov", udta);
        var mdat = Box("mdat", BufferFilled(MdatPayloadLength, 0x33));
        return Concat(ftyp, moov, mdat);
    }

    private const int MdatPayloadLength = 16;

    private static byte[] BuildExifPayload()
    {
        var tiffHeader = Concat(Ascii("MM"), U16(0x002A), U32(0x00000008));
        var ifd0 = Concat(U16(1), U16(0x010F), U16(2), U32(0x0000000A), U32(0x0000001A), U32(0x00000000));
        var makeData = Ascii(MakeMarker + "\0");
        var app1Data = Concat(Ascii("Exif\0\0"), tiffHeader, ifd0, makeData);
        var app1Length = U16((ushort)(2 + app1Data.Length));
        return Concat(new byte[] { 0xFF, 0xD8 }, new byte[] { 0xFF, 0xE1 }, app1Length, app1Data);
    }

    private static void AssertPanaLayout(byte[] data)
    {
        var boxes = ParseBoxes(data);
        Assert.Equal(new[] { "ftyp", "moov", "mdat" }, boxes.Select(b => b.Type).ToArray());
        var moov = boxes[1];
        var moovPayload = data[(moov.Offset + 8)..(moov.Offset + moov.Length)];
        var moovChildren = ParseBoxes(moovPayload);
        Assert.Equal(new[] { "udta" }, moovChildren.Select(b => b.Type).ToArray());
        var udta = moovChildren[0];
        var udtaPayload = moovPayload[(udta.Offset + 8)..(udta.Offset + udta.Length)];
        var udtaChildren = ParseBoxes(udtaPayload);
        Assert.Equal(new[] { "PANA" }, udtaChildren.Select(b => b.Type).ToArray());
        var pana = udtaChildren[0];
        var panaData = udtaPayload[(pana.Offset + 8)..(pana.Offset + pana.Length)];
        var makeString = Encoding.ASCII.GetString(panaData, 0, MakeMarker.Length);
        Assert.Equal(MakeMarker, makeString);
        Assert.Equal(0xFF, panaData[0x10]);
        Assert.Equal(0xD8, panaData[0x11]);
        Assert.Equal(0xFF, panaData[0x12]);
        Assert.Equal(0xE1, panaData[0x13]);
    }

    private static List<(string Type, int Offset, int Length)> ParseBoxes(byte[] data)
    {
        var boxes = new List<(string, int, int)>();
        var pos = 0;
        while (pos + 8 <= data.Length)
        {
            var length = (int)ReadU32(data, pos);
            Assert.True(length >= 8 && pos + length <= data.Length, $"盒结构越界：pos={pos}, length={length}, file={data.Length}");
            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            boxes.Add((type, pos, length));
            pos += length;
        }
        Assert.Equal(data.Length, pos);
        return boxes;
    }

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
        foreach (var part in rest) { part.CopyTo(result, offset); offset += part.Length; }
        return result;
    }
    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var part in parts) { part.CopyTo(result, offset); offset += part.Length; }
        return result;
    }
    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
    private static byte[] Fourcc(string text) => Ascii(text);
    private static byte[] U16(ushort value) => new byte[] { (byte)(value >> 8), (byte)(value & 0xFF) };
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
