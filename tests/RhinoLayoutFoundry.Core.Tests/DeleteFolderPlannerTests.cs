using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class DeleteFolderPlannerTests
{
    private readonly DeleteFolderPlanner _planner = new();

    [Fact]
    public void EmptyFolderCanBeDeleted()
    {
        var snapshot = TestSnapshots.Create() with
        {
            Sheets = new Dictionary<Guid, SheetSnapshot>(),
        };

        var plan = _planner.Plan(Request(), snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(
            new DeleteFolderChange(
                TestSnapshots.ChildFolderId,
                TestSnapshots.RootFolderId,
                "Plans"),
            AssertChange(plan));
    }

    [Fact]
    public void FolderContainingSheetsIsRejected()
    {
        var plan = _planner.Plan(Request(), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.has_sheets");
    }

    [Fact]
    public void FolderContainingNestedFolderIsRejected()
    {
        var nestedId = Guid.NewGuid();
        var snapshot = TestSnapshots.Create() with
        {
            Folders = new Dictionary<Guid, FolderRecord>(TestSnapshots.Create().Folders)
            {
                [nestedId] = new(nestedId, TestSnapshots.ChildFolderId, "Nested", 0),
            },
            Sheets = new Dictionary<Guid, SheetSnapshot>(),
        };

        var plan = _planner.Plan(Request(), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.has_children");
    }

    [Fact]
    public void RootCannotBeDeleted()
    {
        var request = Request() with
        {
            FolderId = TestSnapshots.RootFolderId,
            ExpectedName = "Root",
        };

        var plan = _planner.Plan(request, TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.root_immutable");
    }

    private static DeleteFolderRequest Request()
    {
        return new DeleteFolderRequest(42, 1, TestSnapshots.ChildFolderId, "Plans");
    }

    private static DeleteFolderChange AssertChange(OperationPlan plan)
    {
        Assert.Single(plan.Changes);
        return (DeleteFolderChange)plan.Changes[0];
    }
}
