using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class PasteCanvasPlacementPlannerTests
{
    [Fact]
    public void FolderGroupTopLeftIsRebasedToTarget()
    {
        var snapshot = Snapshot();
        var target = new ObserverPointRecord(420, -75);

        var canvas = new PasteCanvasPlacementPlanner().Place(
            snapshot,
            [TestSnapshots.ChildFolderId],
            [],
            target);
        var layout = new ObserverPlacementPlanner().Arrange(snapshot with { CanvasState = canvas });
        var pasted = layout.Folders[TestSnapshots.ChildFolderId].Bounds;

        Assert.Equal(target.X, pasted.X, 6);
        Assert.Equal(target.Y, pasted.Y, 6);
    }

    [Fact]
    public void StandaloneSheetTopLeftIsRebasedToTarget()
    {
        var snapshot = Snapshot();
        var target = new ObserverPointRecord(-160, 230);

        var canvas = new PasteCanvasPlacementPlanner().Place(
            snapshot,
            [],
            [TestSnapshots.SheetTwoId],
            target);
        var layout = new ObserverPlacementPlanner().Arrange(snapshot with { CanvasState = canvas });
        var pasted = layout.Sheets[TestSnapshots.SheetTwoId].Bounds;

        Assert.Equal(target.X, pasted.X, 6);
        Assert.Equal(target.Y, pasted.Y, 6);
    }

    private static ObserverSnapshot Snapshot() => new(
        42,
        1,
        "Test",
        TestSnapshots.RootFolderId,
        [
            new ObserverFolderSnapshot(TestSnapshots.RootFolderId, null, "Root", 0),
            new ObserverFolderSnapshot(
                TestSnapshots.ChildFolderId,
                TestSnapshots.RootFolderId,
                "Plans copy",
                0),
            new ObserverFolderSnapshot(
                TestSnapshots.OtherFolderId,
                TestSnapshots.RootFolderId,
                "Details",
                1),
        ],
        [
            Sheet(TestSnapshots.SheetOneId, TestSnapshots.ChildFolderId, 0),
            Sheet(TestSnapshots.SheetTwoId, TestSnapshots.OtherFolderId, 0),
        ],
        ObserverCanvasState.Empty);

    private static ObserverSheetSnapshot Sheet(Guid id, Guid folderId, int order) => new(
        id,
        folderId,
        $"Sheet {order + 1}",
        order,
        420,
        297,
        "Millimeters",
        [],
        true,
        1);
}
