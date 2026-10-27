using PhotoPrivacy.Core.Configuration;

namespace PhotoPrivacy.Core.Tests.Configuration;

/// <summary>
/// 新旧 UI 共用的 rules.json 契约（#56）。
/// fixture 是 Rust 侧 `photoprivacy-rules` 写出的形态；旧 UI 的
/// <see cref="FormatRulesStore"/> 必须逐键读回，否则用户在新 UI 保存的规则会在旧 UI 静默回退默认。
/// </summary>
public sealed class FormatRulesStoreTests
{
    private static readonly string[] Families = ["jpeg", "raw", "video", "pdf", "eps"];

    private static readonly string[] Groups =
    [
        "strip_all", "preserve_icc", "strip_exif", "strip_xmp", "strip_iptc", "strip_time",
    ];

    // 防：新 UI 写出的键名/类型与旧 UI 读端漂移，旧 UI 全量回退默认、用户规则被静默丢弃（#56）。
    [Fact]
    public void Load_Reads_Rules_Written_By_The_New_Ui()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pp-rules-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var fixture = Path.Combine(
                FindRepoRoot(),
                "ui", "crates", "photoprivacy-rules", "testdata", "rules.golden.json");
            File.Copy(fixture, Path.Combine(dir, "rules.json"));

            var rules = new FormatRulesStore(dir).Load();

            Assert.Equal(Families.Length * Groups.Length, rules.Count);
            foreach (var family in Families)
            {
                foreach (var group in Groups)
                {
                    Assert.True(
                        rules.ContainsKey(FormatRulesStore.Key(family, group)),
                        $"missing {family}_{group}");
                }
            }

            Assert.False(rules["jpeg_strip_all"]);
            Assert.True(rules["jpeg_strip_exif"]);
            Assert.True(rules["raw_preserve_icc"]);
            Assert.False(rules["video_strip_time"]);
            Assert.False(rules["pdf_strip_all"]);
            Assert.True(rules["eps_strip_time"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PhotoPrivacy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root from test runtime directory.");
    }
}
