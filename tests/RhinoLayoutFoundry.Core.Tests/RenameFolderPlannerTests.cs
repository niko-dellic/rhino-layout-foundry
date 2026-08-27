using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class RenameFolderPlannerTests
{
    private readonly RenameFolderPlanner _planner = new();

    [Fact]
    public void ExistingFolderCanBeRenamed()
    {
        var plan = _planner.Plan(Request("Plans", "Floor Plans"), TestSnapshots.Create());

        Assert.True(plan.CanApply);
        Assert.Equal(
            new RenameFolderChange(
                TestSnapshots.ChildFolderId,
                TestSnapshots.RootFolderId,
                "Plans",
                "Floor Plans"),
            AssertChange(plan));
    }

    [Fact]
    public void NewNameIsTrimmed()
    {
        var plan = _planner.Plan(Request("Plans", "  Floor Plans  "), TestSnapshots.Create());

        Assert.Equal("Floor Plans", AssertChange(plan).NewName);
    }

    [Fact]
    public void RootCannotBeRenamed()
    {
        var request = Request("Root", "Projects") with { FolderId = TestSnapshots.RootFolderId };

        var plan = _planner.Plan(request, TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.root_immutable");
    }

    [Fact]
    public void DuplicateSiblingNameIsRejected()
    {
        var plan = _planner.Plan(Request("Plans", "details"), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.duplicate_name");
    }

    [Fact]
    public void ChangedBeforeValueIsRejected()
    {
        var plan = _planner.Plan(Request("Old Plans", "Floor Plans"), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.before_value_changed");
    }

    private static RenameFolderRequest Request(string expectedName, string newName)
    {
        return new RenameFolderRequest(
            42,
            1,
            TestSnapshots.ChildFolderId,
            expectedName,
            newName);
    }

    private static RenameFolderChange AssertChange(OperationPlan plan)
    {
        Assert.Single(plan.Changes);
        return (RenameFolderChange)plan.Changes[0];
    }
}
