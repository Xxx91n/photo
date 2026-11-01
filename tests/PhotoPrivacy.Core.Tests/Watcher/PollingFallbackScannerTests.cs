using System.Collections.Concurrent;
using PhotoPrivacy.Core.Watcher;

namespace PhotoPrivacy.Core.Tests.Watcher;

public sealed class PollingFallbackScannerTests
{
    /// <summary>include_subdirectories=false 时兜底不上报子目录新文件，顶层照报（#109）。</summary>
    [Fact]
    public async Task IncludeSubdirectories_False_Should_Not_Report_Files_Below_The_Top_Level()
    {
        // 防的具体回潮形态：轮询兜底快照把 SearchOption 写死成 AllDirectories——FSW 明明只监听顶层，
        // 兜底却把监听范围外新建的子目录文件报进处理队列，include_subdirectories=false 形同虚设（#109）。
        var root = Path.Combine(Path.GetTempPath(), "pp-109-poll-top-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            var reported = new ConcurrentBag<string>();
            using var scanner = new PollingFallbackScanner(
                root,
                path =>
                {
                    reported.Add(path);
                    return Task.CompletedTask;
                },
                _ => false,
                TimeSpan.FromSeconds(1),
                includeSubdirectories: false);
            scanner.Start();

            // 初始快照之后落地：顶层的新文件必须报，子目录的新文件不得报。
            await File.WriteAllTextAsync(Path.Combine(root, "in-scope.jpg"), "in-scope");
            await File.WriteAllTextAsync(Path.Combine(nested, "out-of-scope.jpg"), "out-of-scope");

            Assert.True(
                await WaitForAsync(() => reported.Contains(Path.Combine(root, "in-scope.jpg")), TimeSpan.FromSeconds(10)),
                "轮询兜底未上报顶层新文件：用例前提不成立");

            await Task.Delay(TimeSpan.FromSeconds(2));
            Assert.DoesNotContain(reported, p => p.Contains("out-of-scope", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>include_subdirectories=true 时兜底照常上报子目录新文件（#109）。</summary>
    [Fact]
    public async Task IncludeSubdirectories_True_Should_Report_New_Nested_Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-109-poll-all-" + Guid.NewGuid().ToString("N"));
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        try
        {
            var reported = new ConcurrentBag<string>();
            using var scanner = new PollingFallbackScanner(
                root,
                path =>
                {
                    reported.Add(path);
                    return Task.CompletedTask;
                },
                _ => false,
                TimeSpan.FromSeconds(1),
                includeSubdirectories: true);
            scanner.Start();

            await File.WriteAllTextAsync(Path.Combine(nested, "in-scope-deep.jpg"), "in-scope-deep");

            Assert.True(
                await WaitForAsync(() => reported.Contains(Path.Combine(nested, "in-scope-deep.jpg")), TimeSpan.FromSeconds(10)),
                "include_subdirectories=true 时轮询兜底未上报子目录新文件");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return condition();
    }
}
