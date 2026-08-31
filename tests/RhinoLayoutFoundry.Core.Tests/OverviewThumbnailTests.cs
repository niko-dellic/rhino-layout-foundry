using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewThumbnailTests
{
    [Fact]
    public void ObserverResolutionUsesDiscreteBuckets()
    {
        Assert.Equal(256, ObserverThumbnailResolution.Select(120));
        Assert.Equal(512, ObserverThumbnailResolution.Select(300));
        Assert.Equal(1024, ObserverThumbnailResolution.Select(700));
        Assert.Equal(2048, ObserverThumbnailResolution.Select(1200));
    }

    [Fact]
    public void ObserverResolutionHysteresisAvoidsBoundaryThrashing()
    {
        Assert.Equal(512, ObserverThumbnailResolution.Select(380, 512));
        Assert.Equal(512, ObserverThumbnailResolution.Select(400, 512));
        Assert.Equal(1024, ObserverThumbnailResolution.Select(430, 512));
        Assert.Equal(512, ObserverThumbnailResolution.Select(170, 512));
        Assert.Equal(256, ObserverThumbnailResolution.Select(150, 512));
    }

    [Fact]
    public void QueueDeduplicatesAndPromotesPriority()
    {
        var queue = new OverviewThumbnailRequestQueue();
        var key = Key(TestSnapshots.SheetOneId);
        queue.Enqueue(new OverviewThumbnailRequest(key, 20));
        queue.Enqueue(new OverviewThumbnailRequest(key, 2));

        var next = queue.TakeNext();

        Assert.Equal(2, next!.Priority);
        Assert.Equal(0, queue.PendingCount);
        queue.Enqueue(new OverviewThumbnailRequest(key, 0));
        Assert.Equal(0, queue.PendingCount);
        queue.Complete(key);
        queue.Enqueue(new OverviewThumbnailRequest(key, 0));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void QueueReconciliationRemovesOnlyIneligiblePendingRequests()
    {
        var queue = new OverviewThumbnailRequestQueue();
        var retained = Key(TestSnapshots.SheetOneId);
        var removed = Key(TestSnapshots.SheetTwoId);
        queue.Enqueue(new OverviewThumbnailRequest(retained, 10));
        queue.Enqueue(new OverviewThumbnailRequest(removed, 10));

        queue.RetainPending(key => key.SheetPageViewId == retained.SheetPageViewId);

        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(retained, queue.TakeNext()!.Key);
    }

    [Fact]
    public void QueueReconciliationDoesNotDisturbInFlightRequests()
    {
        var queue = new OverviewThumbnailRequestQueue();
        var key = Key(TestSnapshots.SheetOneId);
        queue.Enqueue(new OverviewThumbnailRequest(key, 10));
        Assert.Equal(key, queue.TakeNext()!.Key);

        queue.RetainPending(_ => false);
        queue.Enqueue(new OverviewThumbnailRequest(key, 1));
        Assert.Equal(0, queue.PendingCount);

        queue.Complete(key);
        queue.Enqueue(new OverviewThumbnailRequest(key, 1));
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public void CacheEvictsLeastRecentlyUsedEntryByCount()
    {
        var cache = new OverviewThumbnailCache(maximumEntryCount: 2, maximumByteCount: 100);
        var first = Key(TestSnapshots.SheetOneId);
        var second = Key(TestSnapshots.SheetTwoId);
        var third = Key(Guid.NewGuid());
        cache.Store(first, [1]);
        cache.Store(second, [2]);
        Assert.True(cache.TryGet(first, out _));

        cache.Store(third, [3]);

        Assert.True(cache.TryGet(first, out _));
        Assert.False(cache.TryGet(second, out _));
        Assert.True(cache.TryGet(third, out _));
    }

    [Fact]
    public void CacheHonorsByteBudgetAndTargetedInvalidation()
    {
        var cache = new OverviewThumbnailCache(maximumEntryCount: 10, maximumByteCount: 5);
        var first = Key(TestSnapshots.SheetOneId);
        var second = Key(TestSnapshots.SheetTwoId);
        cache.Store(first, [1, 2, 3]);
        cache.Store(second, [4, 5, 6]);
        Assert.Equal(1, cache.Count);
        cache.Invalidate(42, new HashSet<Guid> { TestSnapshots.SheetTwoId });
        Assert.Equal(0, cache.Count);
        Assert.Equal(0L, cache.ByteCount);
    }

    [Fact]
    public void CacheKeepsPreviewBackgroundVariantsSeparate()
    {
        var cache = new OverviewThumbnailCache(maximumEntryCount: 10, maximumByteCount: 100);
        var sheetId = TestSnapshots.SheetOneId;
        var white = new OverviewThumbnailKey(42, sheetId, 72, 48, BackgroundArgb: 0xFFFFFFFF);
        var warm = new OverviewThumbnailKey(42, sheetId, 72, 48, BackgroundArgb: 0xFFF5F0E8);

        cache.Store(white, [1]);
        cache.Store(warm, [2]);

        Assert.True(cache.TryGet(white, out var whiteBytes));
        Assert.True(cache.TryGet(warm, out var warmBytes));
        Assert.Equal(1, Assert.Single(whiteBytes));
        Assert.Equal(2, Assert.Single(warmBytes));
    }

    private static OverviewThumbnailKey Key(Guid sheetId)
    {
        return new OverviewThumbnailKey(42, sheetId, 72, 48);
    }
}
