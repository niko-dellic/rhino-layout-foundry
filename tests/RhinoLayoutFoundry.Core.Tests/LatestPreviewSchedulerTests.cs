using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class LatestPreviewSchedulerTests
{
    [Fact]
    public async Task CoalescesRequestsAndRejectsStaleResults()
    {
        var scheduler = new LatestPreviewScheduler();
        var release = new TaskCompletionSource();
        var started = new List<long>();
        var shown = new List<long>();
        async Task Render(long version)
        {
            started.Add(version);
            if (version == 1) await release.Task;
            if (scheduler.IsCurrent(version)) shown.Add(version);
        }
        var first = scheduler.RequestAsync(Render, CancellationToken.None, exception => throw exception);
        await scheduler.RequestAsync(Render, CancellationToken.None, exception => throw exception);
        await scheduler.RequestAsync(Render, CancellationToken.None, exception => throw exception);
        release.SetResult();
        await first;
        Assert.Equal(new long[] { 1, 3 }, started);
        Assert.Equal(new long[] { 3 }, shown);
    }

    [Fact]
    public async Task CancellationStopsQueuedWorkAndDoesNotReportAnError()
    {
        var scheduler = new LatestPreviewScheduler();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await scheduler.RequestAsync(_ => throw new InvalidOperationException(), cancellation.Token,
            _ => throw new InvalidOperationException());
    }
}
