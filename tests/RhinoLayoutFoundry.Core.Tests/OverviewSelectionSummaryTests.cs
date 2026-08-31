using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewSelectionSummaryTests
{
    [Fact]
    public void EmptySelectionHasCalmDefaultCopy()
    {
        var summary = OverviewSelectionSummary.Create([]);

        Assert.Equal("No selection", summary.DisplayText);
        Assert.Equal(0, summary.TotalCount);
    }

    [Fact]
    public void DuplicateKeysAreCountedOnce()
    {
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId);

        var summary = OverviewSelectionSummary.Create([key, key]);

        Assert.Equal("1 sheet selected", summary.DisplayText);
        Assert.Equal(1, summary.TotalCount);
    }

    [Fact]
    public void AppearanceStateKeysAreNamedAndCounted()
    {
        var layerState = new OverviewNodeKey(OverviewNodeKind.LayerState, Guid.NewGuid());
        var objectState = new OverviewNodeKey(OverviewNodeKind.ObjectDisplayState, Guid.NewGuid());

        var summary = OverviewSelectionSummary.Create([layerState, objectState]);

        Assert.Equal("1 layer state · 1 object state selected", summary.DisplayText);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(1, summary.LayerStateCount);
        Assert.Equal(1, summary.ObjectDisplayStateCount);
    }
}
