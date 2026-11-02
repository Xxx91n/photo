using PhotoPrivacy.Core.Pipeline;

namespace PhotoPrivacy.Core.Tests.Pipeline;

public sealed class FileProcessedRecordStoreTests
{
    private static string WriteTempStore(string ndjsonContent)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-fpr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "processed.ndjson"), ndjsonContent);
        return dir;
    }

    [Fact]
    public void Recover_Should_Normalize_Legacy_Fingerprint_Keys_To_Path()
    {
        // 防的具体回潮形态：#133 路线 2 前写入的记录是「路径|长度|mtimeTicks」指纹 key；
        // 若不归一化为纯路径，升级后首轮整目录补扫会把已清洗文件按新 key 判定为未处理而重复清洗。
        // 关联 issue #133。
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "legacy", "a.jpg"));
        var legacyKey = $"{path}|1234|638000000000000000";
        var ts = DateTimeOffset.UtcNow.ToString("O");
        var dir = WriteTempStore($$"""{"key":"{{legacyKey}}","ts":"{{ts}}"}""" + "\n");

        try
        {
            using var store = new FileProcessedRecordStore(dir, ttl: TimeSpan.FromDays(7));
            Assert.True(store.IsProcessed(path), "旧指纹格式记录应归一化为路径后命中");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Recover_Should_Keep_Path_Only_Keys_Untouched()
    {
        // 防的具体回潮形态：新口径纯路径 key 被误归一化（例如路径恰好以 |数字|数字 结尾被剥掉）。
        // 关联 issue #133。
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "plain", "a.jpg"));
        var ts = DateTimeOffset.UtcNow.ToString("O");
        var dir = WriteTempStore($$"""{"key":"{{path}}","ts":"{{ts}}"}""" + "\n");

        try
        {
            using var store = new FileProcessedRecordStore(dir, ttl: TimeSpan.FromDays(7));
            Assert.True(store.IsProcessed(path), "纯路径 key 应原样命中");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
