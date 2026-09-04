using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class OverviewPanelPresentationTests
{
    [Fact]
    public void EmptyUserFolderMakesHierarchyVisibleWithoutSheets()
    {
        var rootId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var overview = new DocumentOverview(
            DocumentRuntimeSerialNumber: 42,
            DocumentName: "Test",
            RootFolderId: rootId,
            Folders: [
                new FolderOverview(Id: rootId, ParentId: null, Name: "Root", Order: 0),
                new FolderOverview(Id: folderId, ParentId: rootId, Name: "Plans", Order: 0),
            ],
                    Sheets: []);

        var presentation = OverviewPanelPresentation.Create(
            overview,
            new OverviewTreeFilter(),
            []);

        Assert.Equal(OverviewContentState.Hierarchy, presentation.ContentState);
    }

    [Fact]
    public void NoDocumentProvidesActionableEmptyStateWithoutFilenameIdentity()
    {
        var presentation = OverviewPanelPresentation.Create(
            DocumentOverview.NoDocument,
            new OverviewTreeFilter(null),
            []);

        Assert.Equal(OverviewContentState.NoDocument, presentation.ContentState);
        Assert.Equal("Open or create a model to begin", presentation.DocumentSummary);
        Assert.Contains("Open or create", presentation.EmptyDescription, StringComparison.Ordinal);
        Assert.False(presentation.DocumentSummary.Contains(
            DocumentOverview.NoDocument.DocumentName,
            StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyDocumentStillRendersItsCollapsibleProjectRoot()
    {
        var rootId = Guid.NewGuid();
        var overview = new DocumentOverview(
            DocumentRuntimeSerialNumber: 12,
            DocumentName: "Empty model",
            RootFolderId: rootId,
            Folders: [new FolderOverview(Id: rootId, ParentId: null, Name: "Unorganized", Order: 0)],
            Sheets: []);

        var presentation = OverviewPanelPresentation.Create(
            overview,
            new OverviewTreeFilter(null),
            []);

        Assert.Equal(OverviewContentState.Hierarchy, presentation.ContentState);
        Assert.Equal("0 sheets · 0 details", presentation.DocumentSummary);
        Assert.Equal(string.Empty, presentation.EmptyTitle);
    }

    [Fact]
    public void PopulatedDocumentReportsCountsAndHierarchy()
    {
        var overview = TestSnapshots.Overview(sheetCount: 2, detailsPerSheet: 3);

        var presentation = OverviewPanelPresentation.Create(
            overview,
            new OverviewTreeFilter(null),
            []);

        Assert.Equal(OverviewContentState.Hierarchy, presentation.ContentState);
        Assert.Equal("2 sheets · 6 details", presentation.DocumentSummary);
        Assert.Equal(string.Empty, presentation.ResultSummary);
    }

    [Fact]
    public void ActiveFilterReportsVisibleCounts()
    {
        var overview = TestSnapshots.Overview(sheetCount: 2, detailsPerSheet: 3);

        var presentation = OverviewPanelPresentation.Create(
            overview,
            new OverviewTreeFilter(null, OverviewFilterKind.Sheets),
            []);

        Assert.Equal("Showing 2 sheets · 0 details", presentation.ResultSummary);
    }

    [Fact]
    public void UnmatchedFilterProvidesNoResultsState()
    {
        var overview = TestSnapshots.Overview(sheetCount: 2, detailsPerSheet: 1);

        var presentation = OverviewPanelPresentation.Create(
            overview,
            new OverviewTreeFilter("definitely-not-present"),
            []);

        Assert.Equal(OverviewContentState.NoMatches, presentation.ContentState);
        Assert.Contains("change", presentation.EmptyDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionSummaryNamesSelectedRowKinds()
    {
        var overview = TestSnapshots.Overview(sheetCount: 2, detailsPerSheet: 1);
        var selection = new[]
        {
            new OverviewNodeKey(OverviewNodeKind.Folder, TestSnapshots.ChildFolderId),
            new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId),
            new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetTwoId),
            new OverviewNodeKey(OverviewNodeKind.Detail, TestSnapshots.DetailOneId),
        };

        var presentation = OverviewPanelPresentation.Create(
            overview,
            new OverviewTreeFilter(null),
            selection);

        Assert.Equal("1 folder · 2 sheets · 1 detail selected", presentation.SelectionSummary);
    }
}
