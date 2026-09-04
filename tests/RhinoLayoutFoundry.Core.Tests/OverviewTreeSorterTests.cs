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
                        new DetailOverview(DetailViewportId: Guid.NewGuid(), Name: "Second detail", Order: 2)).ToArray(),
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
                new SheetOverview(PageViewId: Guid.NewGuid(), FolderId: TestSnapshots.RootFolderId, Name: "Page 10", Order: 0, Details: []),
                new SheetOverview(PageViewId: Guid.NewGuid(), FolderId: TestSnapshots.RootFolderId, Name: "Page 2", Order: 1, Details: []),
                new SheetOverview(PageViewId: Guid.NewGuid(), FolderId: TestSnapshots.RootFolderId, Name: "Page 1", Order: 2, Details: []),
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

    [Fact]
    public void DisplayModeSortPreservesDetailOrderWithinEachLayout()
    {
        var sheetId = Guid.NewGuid();
        var overview = TestSnapshots.Overview(sheetCount: 0, detailsPerSheet: 0) with
        {
            Sheets =
            [
                new SheetOverview(
                    PageViewId:                     sheetId,
                    FolderId:                     TestSnapshots.RootFolderId,
                    Name:                     "Page 1",
                    Order:                     0,
                    Details:                     [
                        new DetailOverview(DetailViewportId: Guid.NewGuid(), Name: "Detail 1", Order: 0, DisplayModeId: Guid.NewGuid(), DisplayModeName: "Zebra"),
                        new DetailOverview(DetailViewportId: Guid.NewGuid(), Name: "Detail 2", Order: 1, DisplayModeId: Guid.NewGuid(), DisplayModeName: "Arctic"),
                    ]),
            ],
        };

        var sorted = OverviewTreeSorter.Sort(
            OverviewTreeBuilder.Build(overview),
            OverviewSortProperty.DisplayMode,
            OverviewSortDirection.Ascending);

        var sheet = Assert.Single(Assert.Single(sorted).Children);
        Assert.Equal(["Detail 1", "Detail 2"], sheet.Children.Select(node => node.Label));
    }
}
