using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class MoveFoldersPlannerTests
{
    private readonly MoveFoldersPlanner _planner = new();

    [Fact]
    public void FolderMovesIntoAnotherFolder()
    {
        var plan = _planner.Plan(
            Request(TestSnapshots.OtherFolderId, TestSnapshots.ChildFolderId),
            TestSnapshots.Create());

        Assert.True(plan.CanApply);
        Assert.Single(plan.Changes);
        Assert.Equal(
            new MoveFolderChange(
                TestSnapshots.ChildFolderId,
                TestSnapshots.RootFolderId,
                TestSnapshots.OtherFolderId,
                0),
            (MoveFolderChange)plan.Changes[0]);
    }

    [Fact]
    public void FolderCannotMoveIntoItself()
    {
        var plan = _planner.Plan(
            Request(TestSnapshots.ChildFolderId, TestSnapshots.ChildFolderId),
            TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.folder_cycle");
    }

    [Fact]
    public void FolderCannotMoveIntoDescendant()
    {
        var descendantId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var snapshot = TestSnapshots.Create();
        var folders = snapshot.Folders.ToDictionary(pair => pair.Key, pair => pair.Value);
        folders[descendantId] = new FolderRecord(descendantId, TestSnapshots.ChildFolderId, "Nested", 0);

        var plan = _planner.Plan(
            Request(descendantId, TestSnapshots.ChildFolderId),
            snapshot with { Folders = folders });

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.folder_cycle");
    }

    [Fact]
    public void NoOpMoveIsRejected()
    {
        var plan = _planner.Plan(
            Request(TestSnapshots.RootFolderId, TestSnapshots.ChildFolderId),
            TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.already_in_folder");
    }

    [Fact]
    public void MissingFolderIsRejected()
    {
        var plan = _planner.Plan(
            Request(TestSnapshots.RootFolderId, Guid.NewGuid()),
            TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.folder_missing");
    }

    private static MoveFoldersRequest Request(Guid destinationId, Guid folderId) =>
        new(42, 1, destinationId, [folderId]);
}
