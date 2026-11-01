using PhotoPrivacy.Core.Watcher;

namespace PhotoPrivacy.Core.Tests.Watcher;

public sealed class WatchFolderEnumerationTests
{
    [Fact]
    public void Nonexistent_Root_Should_Enumerate_Nothing()
    {
        // 防的具体回潮形态：枚举入口把「目录不存在」当异常路径抛出——启动首扫/快照/恢复补扫
        // 在监控目录尚未建出时（#124 之后目录只在运行时创建）就会把 worker 启动路径打红（#109）。
        var missing = Path.Combine(Path.GetTempPath(), "pp-109-missing-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(WatchFolderEnumeration.EnumerateFiles(missing, includeSubdirectories: true));
        Assert.Empty(WatchFolderEnumeration.EnumerateFiles(missing, includeSubdirectories: false));
    }

    [Fact]
    public void IncludeSubdirectories_False_Should_Enumerate_Only_The_Top_Level()
    {
        // 防的具体回潮形态：调用方再各写一份 SearchOption.AllDirectories，深度与 FSW 监听深度分叉，
        // include_subdirectories=false 时监听范围外的子目录文件被排进队列真实改写（#109）。
        var root = Path.Combine(Path.GetTempPath(), "pp-109-top-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(root, "top.jpg"), "top");
            File.WriteAllText(Path.Combine(nested, "deep.jpg"), "deep");

            var files = WatchFolderEnumeration.EnumerateFiles(root, includeSubdirectories: false).ToArray();

            var top = Assert.Single(files);
            Assert.Equal(Path.Combine(root, "top.jpg"), top);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncludeSubdirectories_True_Should_Enumerate_Nested_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-109-all-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(root, "top.jpg"), "top");
            File.WriteAllText(Path.Combine(nested, "deep.jpg"), "deep");

            var files = WatchFolderEnumeration.EnumerateFiles(root, includeSubdirectories: true).ToArray();

            Assert.Equal(2, files.Length);
            Assert.Contains(files, p => p.EndsWith("deep.jpg", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
