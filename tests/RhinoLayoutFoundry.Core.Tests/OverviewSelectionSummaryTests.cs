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
        var firstState = new OverviewNodeKey(OverviewNodeKind.AppearanceState, Guid.NewGuid());
        var secondState = new OverviewNodeKey(OverviewNodeKind.AppearanceState, Guid.NewGuid());

        var summary = OverviewSelectionSummary.Create([firstState, secondState]);

        Assert.Equal("2 appearance states selected", summary.DisplayText);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(2, summary.AppearanceStateCount);
    }
}
