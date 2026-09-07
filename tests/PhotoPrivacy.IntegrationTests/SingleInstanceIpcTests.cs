using PhotoPrivacy.Ui;
using PhotoPrivacy.Ui.Services;

namespace PhotoPrivacy.IntegrationTests;

public sealed class UiSingleInstanceTests
{
    [Fact]
    public async Task RunShowWindowServerAsync_Should_Stop_Quickly_When_Cancelled_Without_Client()
    {
        using var cts = new CancellationTokenSource();
        var uniquePipeName = "PhotoPrivacyUi_ShowWindow_Test_" + Guid.NewGuid().ToString("N");
        var task = UiSingleInstance.RunShowWindowServerAsync(
            onShowWindowRequested: () => { },
            cancellationToken: cts.Token,
            pipeName: uniquePipeName);

        await Task.Delay(100);
        await cts.CancelAsync();

        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(task, completed);
    }

    [Fact]
    public async Task RunShowWindowServerAsync_Should_Stop_Quickly_When_Cancelled_After_One_Message()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var uniquePipeName = "PhotoPrivacyUi_ShowWindow_Test_" + Guid.NewGuid().ToString("N");
        var task = UiSingleInstance.RunShowWindowServerAsync(
            onShowWindowRequested: () => Interlocked.Increment(ref calls),
            cancellationToken: cts.Token,
            pipeName: uniquePipeName);

        // 票 28 返修三轮：服务端管道在 CI 冷启动下就绪时机不定（run 33944932480 实锤单次连接 flaky），
        // 连接改为有界重试直至服务端就绪（每轮内部自带 500ms 连接超时），测试语义不变：消息确实送达一次。
        var sent = false;
        var connectDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < connectDeadline && !(sent = await UiSingleInstance.NotifyExistingInstanceAsync(pipeName: uniquePipeName)))
        {
            await Task.Delay(100);
        }

        Assert.True(sent);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        while (DateTime.UtcNow < deadline && Volatile.Read(ref calls) == 0)
        {
            await Task.Delay(20);
        }

        Assert.Equal(1, Volatile.Read(ref calls));

        await cts.CancelAsync();
        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(task, completed);
    }
}
