using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class FolderCreationDestinationTests
{
    private readonly DocumentOverview _overview = TestSnapshots.Overview(2, 1) with
    {
        Folders =
        [
            new FolderOverview(TestSnapshots.RootFolderId, null, "Root", 0),
            new FolderOverview(TestSnapshots.ChildFolderId, TestSnapshots.RootFolderId, "Plans", 0),
        ],
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
    public void SheetOrMultipleSelectionCreatesAtRoot()
    {
        var sheet = FolderCreationDestination.Resolve(
            _overview,
            [new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId)]);
        var multiple = FolderCreationDestination.Resolve(
            _overview,
            [
                new OverviewNodeKey(OverviewNodeKind.Folder, TestSnapshots.ChildFolderId),
                new OverviewNodeKey(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId),
            ]);

        Assert.Equal(TestSnapshots.RootFolderId, sheet!.ParentFolderId);
        Assert.Equal(TestSnapshots.RootFolderId, multiple!.ParentFolderId);
    }
}
