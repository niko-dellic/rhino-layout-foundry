using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewSelectionModelTests
{
    private static readonly OverviewNodeKey Folder = new(
        OverviewNodeKind.Folder,
        Guid.Parse("70000000-0000-0000-0000-000000000001"));
    private static readonly OverviewNodeKey SheetOne = new(
        OverviewNodeKind.Sheet,
        TestSnapshots.SheetOneId);
    private static readonly OverviewNodeKey DetailOne = new(
        OverviewNodeKind.Detail,
        TestSnapshots.DetailOneId);
    private static readonly OverviewNodeKey SheetTwo = new(
        OverviewNodeKind.Sheet,
        TestSnapshots.SheetTwoId);

    [Fact]
    public void HiddenSelectionSurvivesFiltering()
    {
        var selection = new OverviewSelectionModel();
        selection.Replace([SheetOne]);

        var visible = selection.VisibleSelection([Folder, SheetTwo]);

        Assert.Empty(visible);
        Assert.Contains(SheetOne, selection.Selected);
    }

    [Fact]
    public void RangeSelectionUsesStableVisibleOrder()
    {
        var selection = new OverviewSelectionModel();
        selection.Replace([SheetOne], SheetOne);

        selection.SelectRange([Folder, SheetOne, DetailOne, SheetTwo], SheetTwo, additive: false);

        Assert.Equal(3, selection.Selected.Count);
        Assert.Contains(SheetOne, selection.Selected);
        Assert.Contains(DetailOne, selection.Selected);
        Assert.Contains(SheetTwo, selection.Selected);
        Assert.Equal(SheetOne, selection.Anchor);
    }

    [Fact]
    public void AdditiveRangePreservesExistingSelection()
    {
        var selection = new OverviewSelectionModel();
        selection.Replace([Folder, SheetOne], SheetOne);

        selection.SelectRange([Folder, SheetOne, DetailOne, SheetTwo], SheetTwo, additive: true);

        Assert.Equal(4, selection.Selected.Count);
        Assert.Contains(Folder, selection.Selected);
        Assert.Contains(SheetOne, selection.Selected);
        Assert.Contains(DetailOne, selection.Selected);
        Assert.Contains(SheetTwo, selection.Selected);
    }

    [Fact]
    public void PruneDropsRowsRemovedFromDocument()
    {
        var selection = new OverviewSelectionModel();
        selection.Replace([SheetOne, SheetTwo]);

        selection.Prune([Folder, SheetTwo]);

        Assert.Single(selection.Selected);
        Assert.Contains(SheetTwo, selection.Selected);
    }

    [Fact]
    public void ToggleAddsAndRemovesOneStableKey()
    {
        var selection = new OverviewSelectionModel();

        selection.Toggle(SheetOne);
        Assert.Contains(SheetOne, selection.Selected);

        selection.Toggle(SheetOne);
        Assert.Empty(selection.Selected);
        Assert.Equal<OverviewNodeKey?>(null, selection.Anchor);
    }
}
