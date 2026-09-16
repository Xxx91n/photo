using System.Text;
using PhotoPrivacy.Ipc;

namespace PhotoPrivacy.IntegrationTests.Ipc;

/// <summary>
/// 票 05（A-005）行为测试：缓冲读（<see cref="IpcLineReader"/>）与旧逐字节读循环语义逐字等价。
///
/// 旧实现被复制在 Worker 服务端（ReadBoundedLineAsync）与 UI 客户端（WorkerIpcClient.ReadBoundedLineAsync）
/// 两处，每读一字节 await 一次；本票收敛为单一实现并改为分块读。
/// 本文件锁定有界语义（换行结束 / 丢弃 CR / 达上限返回 null / EOF 结束 / 分块边界）。
/// </summary>
public sealed class IpcLineReaderTests
{
    [Fact]
    public async Task Should_Read_Up_To_Lf()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream("abc\ndef"), 64, CancellationToken.None);

        Assert.Equal("abc", line);
    }

    [Fact]
    public async Task Should_Drop_All_Carriage_Returns()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream("a\rb\r\n"), 64, CancellationToken.None);

        Assert.Equal("ab", line);
    }

    [Fact]
    public async Task Should_Return_Remainder_At_Eof_Without_Lf()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream("tail"), 64, CancellationToken.None);

        Assert.Equal("tail", line);
    }

    [Fact]
    public async Task Should_Return_Empty_String_For_Empty_Stream()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream(string.Empty), 64, CancellationToken.None);

        Assert.Equal(string.Empty, line);
    }

    [Fact]
    public async Task Should_Return_Null_When_Content_Reaches_MaxBytes()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream("abcd"), 4, CancellationToken.None);

        Assert.Null(line);
    }

    [Fact]
    public async Task Should_Accept_Line_Of_MaxBytes_Minus_One_Plus_Lf()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream("abc\n"), 4, CancellationToken.None);

        Assert.Equal("abc", line);
    }

    [Fact]
    public async Task Should_Read_First_Line_Of_Audit_Shaped_Payload_Spanning_Multiple_Chunks()
    {
        // 覆盖分块边界：单块 4096 字节，这里给 50 行、每行约 400 字节的审计 JSONL 形状负载（> 一块）。
        var lines = Enumerable.Range(0, 50)
            .Select(i => $"{{\"n\":{i},\"pad\":\"{new string('x', 380)}\"}}")
            .ToArray();
        var payload = string.Join("\n", lines);

        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream(payload + "\n"), 64 * 1024, CancellationToken.None);

        Assert.Equal(lines[0], line);
    }

    [Fact]
    public async Task Should_Not_Read_Past_The_Line_Boundary()
    {
        var line = await IpcLineReader.ReadBoundedLineAsync(NewStream("first\nsecond\n"), 1024, CancellationToken.None);

        Assert.Equal("first", line);
    }

    private static MemoryStream NewStream(string text) => new(Encoding.UTF8.GetBytes(text));
}
