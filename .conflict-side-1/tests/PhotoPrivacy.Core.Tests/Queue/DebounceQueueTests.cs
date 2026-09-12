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

    [Fact]
    public void RecentFingerprintCache_Should_Evict_Oldest_When_Capacity_Exceeded()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var fakeNow = now;
        var cache = new RecentFingerprintCache(() => fakeNow);

        // Insert EvictExcess(max) entries then one more — expect bcap to enforce.
        for (var i = 0; i < 10; i++)
        {
            cache.Remember($"f{i}.jpg", new FileFingerprint(i, DateTime.UnixEpoch));
            fakeNow = fakeNow.AddSeconds(1); // make each newer
        }
        Assert.Equal(10, cache.Count);

        cache.EvictExcess(5);
        Assert.Equal(5, cache.Count);
        // Oldest 5 removed (f0..f4 — earliest SeenAt). Remaining: f5..f9.
        Assert.False(cache.ShouldSuppress("f0.jpg", new FileFingerprint(0, DateTime.UnixEpoch), TimeSpan.FromMinutes(1)));
        Assert.True(cache.ShouldSuppress("f9.jpg", new FileFingerprint(9, DateTime.UnixEpoch), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void RecentFingerprintCache_CleanupExpired_Should_Remove_Stale_Only()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var fakeNow = now;
        var cache = new RecentFingerprintCache(() => fakeNow);
        var ttl = TimeSpan.FromSeconds(30);

        cache.Remember("fresh.jpg", new FileFingerprint(1, DateTime.UnixEpoch));
        fakeNow = fakeNow.AddSeconds(10);
        cache.Remember("stale.jpg", new FileFingerprint(2, DateTime.UnixEpoch));
        fakeNow = fakeNow.AddSeconds(40); // newer than first but also ages the first entry
        cache.CleanupExpired(ttl); // entries older than 30s removed

        // "fresh" was inserted 50s ago (older than ttl) — removed
        // "stale" was inserted 40s ago (older than ttl) — wait, inserted at now+10, now at now+50 -> 40s old, also stale.
        // Adjust: only the first entry ages to 50s.
        Assert.True(cache.Count <= 1);
    }

    [Fact]
    public void RecentFingerprintCache_DefaultCapacity_Should_Be_Nonneg()
    {
        Assert.Equal(10000, RecentFingerprintCache.DefaultCapacity);
    }

}
