using System.Text;

namespace PhotoPrivacy.Ipc;

/// <summary>
/// 票 05（A-005）：有界行读取的唯一实现。
///
/// 原实现（Worker 服务端与 UI 客户端各复制一份）每读一个字节就 await 一次
/// <c>Stream.ReadAsync</c>——GetRecentLogs 返回满 50 行审计时约六万次 await + 六万次 syscall。
/// 现改为分块读（4 KB）后在块内扫描换行，并在两处消除重复实现。
///
/// 语义与旧实现逐字对齐（行为测试 IpcLineReaderTests 锁定）：
/// 遇到 LF 结束一行；行内全部 CR 被丢弃；内容长度达到 <paramref name="maxBytes"/> 上限时返回 null
/// （超长消息拒绝，调用方据此丢弃连接）；EOF 结束（允许空串）。协议未变：仍是一连接一消息、
/// 换行/EOF 定界，无帧化、无传输复用（ADR 0057 裁决面未动）。
/// </summary>
public static class IpcLineReader
{
    private const int ReadChunkBytes = 4096;

    public static async Task<string?> ReadBoundedLineAsync(
        Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var accumulated = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                var value = chunk[i];
                if (value == (byte)'\n')
                {
                    return Decode(accumulated);
                }

                if (value != (byte)'\r')
                {
                    accumulated.WriteByte(value);
                    if (accumulated.Length >= maxBytes)
                    {
                        return null;
                    }
                }
            }
        }

        return Decode(accumulated);
    }

    private static string Decode(MemoryStream accumulated)
        => Encoding.UTF8.GetString(accumulated.ToArray());
}
