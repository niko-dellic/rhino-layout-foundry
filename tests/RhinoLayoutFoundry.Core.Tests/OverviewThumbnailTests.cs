using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewThumbnailTests
{
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

    private static OverviewThumbnailKey Key(Guid sheetId)
    {
        return new OverviewThumbnailKey(42, sheetId, 72, 48);
    }
}
