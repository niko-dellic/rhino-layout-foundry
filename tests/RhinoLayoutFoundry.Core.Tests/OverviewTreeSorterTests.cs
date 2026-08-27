using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewTreeSorterTests
{
    [Fact]
    public void NameSortKeepsDocumentRootFixedAndSortsEverySiblingGroup()
    {
        var overview = TestSnapshots.Overview(sheetCount: 3, detailsPerSheet: 2);
        overview = overview with
        {
            Sheets = overview.Sheets.Reverse().Select(sheet => sheet with
            {
                Details = sheet.Details.Reverse().ToArray(),
            }).ToArray(),
        };

        var sorted = OverviewTreeSorter.Sort(
            OverviewTreeBuilder.Build(overview),
            OverviewSortProperty.Name,
            OverviewSortDirection.Ascending);

        var root = Assert.Single(sorted);
        Assert.True(root.IsDocumentRoot);
        Assert.Equal(["A-001", "A-002", "A-003"], root.Children.Select(node => node.Label));
        Assert.Equal(["Detail 1", "Detail 2"], root.Children[0].Children.Select(node => node.Label));
    }

    [Fact]
    public void DetailCountSortSupportsDescendingDirection()
    {
        var overview = TestSnapshots.Overview(sheetCount: 2, detailsPerSheet: 1);
        overview = overview with
        {
            Sheets =
            [
                overview.Sheets[0],
                overview.Sheets[1] with
                {
                    Details = overview.Sheets[1].Details.Append(
                        new DetailOverview(Guid.NewGuid(), "Second detail", 2)).ToArray(),
                },
            ],
        };

        var sorted = OverviewTreeSorter.Sort(
            OverviewTreeBuilder.Build(overview),
            OverviewSortProperty.DetailCount,
            OverviewSortDirection.Descending);

        Assert.Equal("A-002", sorted[0].Children[0].Label);
        Assert.Equal("A-001", sorted[0].Children[1].Label);
    }

    [Fact]
    public void NameSortUsesFinderStyleNumericOrdering()
    {
        var overview = TestSnapshots.Overview(sheetCount: 0, detailsPerSheet: 0) with
        {
            Sheets =
            [
                new SheetOverview(Guid.NewGuid(), TestSnapshots.RootFolderId, "Page 10", 0, [], []),
                new SheetOverview(Guid.NewGuid(), TestSnapshots.RootFolderId, "Page 2", 1, [], []),
                new SheetOverview(Guid.NewGuid(), TestSnapshots.RootFolderId, "Page 1", 2, [], []),
            ],
        };

        var sorted = OverviewTreeSorter.Sort(OverviewTreeBuilder.Build(overview),
            OverviewSortProperty.Name, OverviewSortDirection.Ascending);

        Assert.Equal(["Page 1", "Page 2", "Page 10"], sorted[0].Children.Select(node => node.Label));
    }

    [Fact]
    public void TemplateSortPlacesRegisteredLayoutsFirst()
    {
        var overview = TestSnapshots.Overview(sheetCount: 2, detailsPerSheet: 1);
        overview = overview with
        {
            Sheets =
            [
                overview.Sheets[0],
                overview.Sheets[1] with { IsTemplate = true },
            ],
        };

        var sorted = OverviewTreeSorter.Sort(OverviewTreeBuilder.Build(overview),
            OverviewSortProperty.Template, OverviewSortDirection.Ascending);

        Assert.True(sorted[0].Children[0].Sheet!.IsTemplate);
        Assert.False(sorted[0].Children[1].Sheet!.IsTemplate);
    }
}
