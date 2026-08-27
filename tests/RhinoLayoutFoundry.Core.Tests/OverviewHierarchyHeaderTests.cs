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
            12,
            "Ignored.3dm",
            rootId,
            [new FolderOverview(rootId, null, "Root", 0)],
            [
                new SheetOverview(
                    Guid.NewGuid(),
                    rootId,
                    "A101",
                    0,
                    [],
                    [
                        new DetailOverview(Guid.NewGuid(), "Plan", 0),
                        new DetailOverview(Guid.NewGuid(), "Section", 1),
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
