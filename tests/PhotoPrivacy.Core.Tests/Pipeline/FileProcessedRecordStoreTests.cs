using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
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

    private static string RecordLine(string key, DateTimeOffset ts, int? version = null)
    {
        // 用 JsonSerializer 而不是字符串拼接：Windows 路径含反斜杠，必须由序列化器转义，
        // 否则 RecoverFromLog 会跳过解析失败的行（#151 审查）。
        if (version is null)
        {
            return JsonSerializer.Serialize(new { key, ts = ts.ToString("O") }) + "\n";
        }

        return JsonSerializer.Serialize(new { key, ts = ts.ToString("O"), v = version.Value }) + "\n";
    }

    private static ConcurrentDictionary<string, DateTimeOffset> ReadCache(FileProcessedRecordStore store)
    {
        // 白盒读取：恢复后的合并时间戳没有公开读口；本用例只验证「同路径多条记录取最新」。
        var field = typeof(FileProcessedRecordStore).GetField(
            "_cache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("FileProcessedRecordStore._cache 不存在");
        return (ConcurrentDictionary<string, DateTimeOffset>)(field.GetValue(store)
            ?? throw new InvalidOperationException("FileProcessedRecordStore._cache 未初始化"));
    }

    [Fact]
    public void Recover_Should_Normalize_Legacy_Fingerprint_Keys_To_Path()
    {
        // 防的具体回潮形态：#133 路线 2 前写入的记录是「路径|长度|mtimeTicks」指纹 key；
        // 若不归一化为纯路径，升级后首轮整目录补扫会把已清洗文件按新 key 判定为未处理而重复清洗。
        // 关联 issue #133。
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "legacy", "a.jpg"));
        var legacyKey = $"{path}|1234|638000000000000000";
        var dir = WriteTempStore(RecordLine(legacyKey, DateTimeOffset.UtcNow));

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
    public void Recover_Should_Keep_Versioned_Path_Only_Key_With_Numeric_Suffix_Untouched()
    {
        // 防的具体回潮形态：新口径纯路径 key 被按尾部「|数字|数字」误判为旧指纹并剥掉。
        // POSIX 文件名允许 |，路径本身可能以该形态结尾；新记录带 v=2 标记后不再做后缀推断。
        // 关联 issue #133。
        var key = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "versioned", "a.jpg")) + "|123|456";
        var dir = WriteTempStore(RecordLine(key, DateTimeOffset.UtcNow, version: 2));

        try
        {
            using var store = new FileProcessedRecordStore(dir, ttl: TimeSpan.FromDays(7));
            Assert.True(store.IsProcessed(key), "带 v 标记的纯路径 key 应原样命中，不应剥掉数字后缀");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Recover_Should_Keep_Unversioned_Path_Only_Keys_Untouched()
    {
        // 无版本标记、也不带指纹后缀的路径键按原样保留（升级前中间态/异常记录）。
        // 关联 issue #133。
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "plain", "a.jpg"));
        var dir = WriteTempStore(RecordLine(path, DateTimeOffset.UtcNow));

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

    [Fact]
    public void Recover_Should_Keep_Latest_Timestamp_When_Same_Path_Has_Multiple_Records()
    {
        // 同一路径在旧指纹口径下可能留下多条记录；归一化后必须保留最新时间戳，
        // 否则较早记录先到期会让仍在 TTL 内的路径键被删除，补扫再次处理文件（#151 审查）。
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dupe", "a.jpg"));
        var older = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = DateTimeOffset.UtcNow.AddHours(-1);
        var content = RecordLine($"{path}|111|638000000000000001", older)
            + RecordLine($"{path}|222|638000000000000002", newer);
        var dir = WriteTempStore(content);

        try
        {
            using var store = new FileProcessedRecordStore(dir, ttl: TimeSpan.FromDays(7));
            var cache = ReadCache(store);
            Assert.True(cache.TryGetValue(path, out var recovered), "同路径记录应归一化到同一个路径键");
            Assert.Equal(newer.ToUniversalTime(), recovered.ToUniversalTime());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MarkProcessed_Should_Write_Versioned_Path_Key_And_Recover()
    {
        // 新记录必须带 v 标记，重启恢复时按纯路径命中；否则未来路径尾部的数字后缀仍会被误剥。
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mark", "a.jpg"));
        var dir = Path.Combine(Path.GetTempPath(), "pp-fpr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            using (var store = new FileProcessedRecordStore(dir, ttl: TimeSpan.FromDays(7)))
            {
                store.MarkProcessed(path);
            }

            var line = File.ReadAllText(Path.Combine(dir, "processed.ndjson"));
            Assert.Contains("\"v\":2", line);

            using var reopened = new FileProcessedRecordStore(dir, ttl: TimeSpan.FromDays(7));
            Assert.True(reopened.IsProcessed(path), "新格式记录重启后应按纯路径命中");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void KeyComparer_Should_Follow_Platform_Case_Sensitivity()
    {
        // Windows 路径大小写不敏感；Unix 类按 Ordinal 区分，避免 a.jpg / A.jpg 共用一个去重身份。
        if (OperatingSystem.IsWindows())
        {
            Assert.True(ProcessedKeyBuilder.KeyComparer.Equals(@"C:\hot\a.jpg", @"C:\hot\A.jpg"));
        }
        else
        {
            Assert.False(ProcessedKeyBuilder.KeyComparer.Equals("/hot/a.jpg", "/hot/A.jpg"));
        }
    }
}
