using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class AddFolderPlannerTests
{
    private static readonly Guid NewFolderId = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private readonly AddFolderPlanner _planner = new();

    [Fact]
    public void RootFolderCreationAppendsAfterSiblingFolders()
    {
        var plan = _planner.Plan(Request("Sections"), TestSnapshots.Create());

        Assert.True(plan.CanApply);
        Assert.Equal(
            new AddFolderChange(NewFolderId, TestSnapshots.RootFolderId, "Sections", 2),
            AssertChange(plan));
    }

    [Fact]
    public void SelectedFolderCanReceiveNestedFolder()
    {
        var plan = _planner.Plan(
            Request("Interiors", TestSnapshots.ChildFolderId),
            TestSnapshots.Create());

        Assert.Equal(
            new AddFolderChange(NewFolderId, TestSnapshots.ChildFolderId, "Interiors", 0),
            AssertChange(plan));
    }

    [Fact]
    public void FolderNameIsTrimmed()
    {
        var plan = _planner.Plan(Request("  Sections  "), TestSnapshots.Create());

        Assert.Equal("Sections", AssertChange(plan).Name);
    }

    [Fact]
    public void EmptyNameIsRejected()
    {
        var plan = _planner.Plan(Request("   "), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.empty_name");
    }

    [Fact]
    public void DuplicateSiblingNameIsRejectedCaseInsensitively()
    {
        var plan = _planner.Plan(Request("plans"), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.duplicate_name");
    }

    [Fact]
    public void MissingParentIsRejected()
    {
        var plan = _planner.Plan(Request("Sections", Guid.NewGuid()), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.parent_missing");
    }

    [Fact]
    public void StaleRevisionIsRejected()
    {
        var plan = _planner.Plan(Request("Sections") with { SourceRevision = 0 }, TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "folder.stale_revision");
    }

    private static AddFolderRequest Request(string name, Guid? parentId = null)
    {
        return new AddFolderRequest(
            42,
            1,
            NewFolderId,
            parentId ?? TestSnapshots.RootFolderId,
            name);
    }

    private static AddFolderChange AssertChange(OperationPlan plan)
    {
        Assert.Single(plan.Changes);
        return (AddFolderChange)plan.Changes[0];
    }
}
