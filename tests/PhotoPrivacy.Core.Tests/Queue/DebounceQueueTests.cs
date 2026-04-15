using PhotoPrivacy.Core.Queue;

namespace PhotoPrivacy.Core.Tests.Queue;

public sealed class DebounceQueueTests
{
    [Fact]
    public void PopReady_Should_Return_Only_One_Item_For_Burst_Events()
    {
        var now = DateTimeOffset.UtcNow;
        var fakeNow = now;
        var queue = new DebounceQueue(TimeSpan.FromMilliseconds(800), () => fakeNow);

        queue.Enqueue(@"D:\hot\a.jpg");
        queue.Enqueue(@"D:\hot\a.jpg");
        queue.Enqueue(@"D:\hot\a.jpg");

        fakeNow = now.AddMilliseconds(900);
        var ready = queue.PopReady();

        Assert.Single(ready);
        Assert.Equal(@"D:\hot\a.jpg", ready[0]);
    }

    [Fact]
    public void RecentFingerprintCache_Should_Suppress_Duplicate_Output_Within_Ttl()
    {
        var now = DateTimeOffset.UtcNow;
        var fakeNow = now;
        var cache = new RecentFingerprintCache(() => fakeNow);
        var fp = new FileFingerprint(1024, new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc));

        cache.Remember(@"D:\hot\a.jpg", fp);
        var suppress = cache.ShouldSuppress(@"D:\hot\a.jpg", fp, TimeSpan.FromSeconds(5));

        Assert.True(suppress);
    }
}
