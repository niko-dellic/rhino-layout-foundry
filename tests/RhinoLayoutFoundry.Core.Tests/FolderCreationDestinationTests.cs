using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class FolderCreationDestinationTests
{
    private readonly DocumentOverview _overview = TestSnapshots.Overview(2, 1) with
    {
        Folders =
        [
            new FolderOverview(Id: TestSnapshots.RootFolderId, ParentId: null, Name: "Root", Order: 0),
            new FolderOverview(Id: TestSnapshots.ChildFolderId, ParentId: TestSnapshots.RootFolderId, Name: "Plans", Order: 0),
        ],
        Sheets = TestSnapshots.Overview(2, 1).Sheets
            .Select((sheet, index) => index == 0
                ? sheet with { FolderId = TestSnapshots.ChildFolderId }
                : sheet)
            .ToArray(),
    };

    [Fact]
    public void NoSelectionCreatesAtRoot()
    {
        var destination = FolderCreationDestination.Resolve(_overview, []);

        Assert.Equal(TestSnapshots.RootFolderId, destination!.ParentFolderId);
        Assert.Equal("Root", destination.DisplayName);
    }

    [Fact]
    public void OneSelectedFolderCreatesNestedFolder()
    {
        var destination = FolderCreationDestination.Resolve(
            _overview,
            [new OverviewNodeKey(OverviewNodeKind.Folder, TestSnapshots.ChildFolderId)]);

        Assert.Equal(TestSnapshots.ChildFolderId, destination!.ParentFolderId);
        Assert.Equal("Plans", destination.DisplayName);
    }

    [Fact]
    public void SheetAndDetailCreateBesideContainingLayout()
    {
        var sheet = FolderCreationDestination.Resolve(
            _overview,
            [new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId)]);
        var detail = FolderCreationDestination.Resolve(
            _overview,
            [new OverviewNodeKey(OverviewNodeKind.Detail, _overview.Sheets[0].Details[0].DetailViewportId)]);

        Assert.Equal(TestSnapshots.ChildFolderId, sheet!.ParentFolderId);
        Assert.Equal(TestSnapshots.ChildFolderId, detail!.ParentFolderId);
        Assert.Equal("Plans", sheet.DisplayName);
    }

    [Fact]
    public void MultipleSelectionCreatesAtRoot()
    {
        var multiple = FolderCreationDestination.Resolve(
            _overview,
            [
                new OverviewNodeKey(OverviewNodeKind.Folder, TestSnapshots.ChildFolderId),
                new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId),
            ]);

        Assert.Equal(TestSnapshots.RootFolderId, multiple!.ParentFolderId);
    }
}
