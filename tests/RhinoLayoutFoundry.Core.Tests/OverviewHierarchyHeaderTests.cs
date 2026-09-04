using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewHierarchyHeaderTests
{
    [Fact]
    public void NoDocumentUsesPlainLayoutsLabel()
    {
        Assert.Equal("Layouts", OverviewHierarchyHeader.Create(DocumentOverview.NoDocument));
    }

    [Fact]
    public void DocumentCountsSitBesideLayoutsLabel()
    {
        var rootId = Guid.NewGuid();
        var overview = new DocumentOverview(
            DocumentRuntimeSerialNumber: 12,
            DocumentName: "Ignored.3dm",
            RootFolderId: rootId,
            Folders: [new FolderOverview(Id: rootId, ParentId: null, Name: "Root", Order: 0)],
            Sheets: [
                new SheetOverview(
                    PageViewId:                     Guid.NewGuid(),
                    FolderId:                     rootId,
                    Name:                     "A101",
                    Order:                     0,
                    Details:                     [
                        new DetailOverview(DetailViewportId: Guid.NewGuid(), Name: "Plan", Order: 0),
                        new DetailOverview(DetailViewportId: Guid.NewGuid(), Name: "Section", Order: 1),
                    ]),
            ]);

        Assert.Equal(
            "Layouts  ·  1 sheet  ·  2 details",
            OverviewHierarchyHeader.Create(overview));
    }

    [Fact]
    public void SelectionSummaryIsShownInTheHierarchyHeader()
    {
        var overview = TestSnapshots.Overview(9, 1);

        Assert.Equal(
            "Layouts  ·  9 sheets  ·  9 details  ·  4 sheets selected",
            OverviewHierarchyHeader.Create(overview, "4 sheets selected"));
    }
}
