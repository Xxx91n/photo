using System.Threading;
using PhotoPrivacy.Core.Configuration;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests.Ui;

/// <summary>
/// 票 27 — ConfigFileWatcher 端到端测试：手改 config.json → 防抖窗口内合并 →
/// onReload 回调触发.收敛 3 处双 DTO 漂移点后 round-trip 仍等价.
/// </summary>
public sealed class ConfigFileWatcherTests
{
    [Fact]
    public void Manual_Edit_Should_Trigger_Reload_After_Debounce_Window()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-cfw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(AppConfig.Default));

        var reloadCount = 0;
        var reloadEvent = new ManualResetEventSlim(false);
        using var watcher = new ConfigFileWatcher(
            configPath,
            onReload: () =>
            {
                Interlocked.Increment(ref reloadCount);
                reloadEvent.Set();
            },
            debounce: TimeSpan.FromMilliseconds(200));
        watcher.Start();

        try
        {
            // 模拟"手改":写入新主题字段
            var modified = AppConfig.Default with
            {
                Ui = AppConfig.Default.Ui with { ThemeVariant = "dark" }
            };
            File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(modified));

            Assert.True(reloadEvent.Wait(TimeSpan.FromSeconds(5)), "ConfigFileWatcher should fire onReload within 5s after manual edit");
            Assert.True(reloadCount >= 1, $"Expected >=1 reload, got {reloadCount}");

            var reloaded = AppConfigLoader.Load(configPath);
            Assert.Equal("dark", reloaded.Ui.ThemeVariant);
        }
        finally
        {
            watcher.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Suppress_Next_Reload_Should_Skip_Self_Write_Trigger()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-cfw-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(AppConfig.Default));

        var reloadCount = 0;
        using var watcher = new ConfigFileWatcher(
            configPath,
            onReload: () => Interlocked.Increment(ref reloadCount),
            debounce: TimeSpan.FromMilliseconds(150));
        watcher.Start();

        try
        {
            // 模拟"自身保存":抑制 + 写盘
            watcher.SuppressNextReload();
            var modified = AppConfig.Default with
            {
                Ui = AppConfig.Default.Ui with { ThemeVariant = "light" }
            };
            File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(modified));

            // 等超过防抖窗口
            Thread.Sleep(800);

            Assert.Equal(0, reloadCount);
        }
        finally
        {
            watcher.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rapid_Edits_Should_Coalesce_Into_Single_Reload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pp-cfw-coalesce-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "config.json");
        File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(AppConfig.Default));

        var reloadCount = 0;
        var reloadEvent = new ManualResetEventSlim(false);
        using var watcher = new ConfigFileWatcher(
            configPath,
            onReload: () =>
            {
                Interlocked.Increment(ref reloadCount);
                reloadEvent.Set();
            },
            debounce: TimeSpan.FromMilliseconds(400));
        watcher.Start();

        try
        {
            // 50ms 内连续 3 次写入,只应合并成 1 次 reload
            for (var i = 0; i < 3; i++)
            {
                var modified = AppConfig.Default with
                {
                    Ui = AppConfig.Default.Ui with { ThemeVariant = i % 2 == 0 ? "dark" : "light" }
                };
                File.WriteAllText(configPath, AppConfigJson.ToIndentedJson(modified));
                Thread.Sleep(50);
            }

            Assert.True(reloadEvent.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(200); // 确认没有更多 reload
            Assert.Equal(1, reloadCount);
        }
        finally
        {
            watcher.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }
}
