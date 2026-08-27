using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class CreateSheetPlannerTests
{
    private readonly CreateSheetPlanner _planner = new();

    [Fact]
    public void SheetIsCreatedInDestinationAndAppended()
    {
        var plan = _planner.Plan(Request("A-003", TestSnapshots.OtherFolderId), TestSnapshots.Create());

        Assert.True(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "sheet.undo_unavailable");
        Assert.Single(plan.Changes);
        Assert.Equal(
            new CreateSheetChange(TestSnapshots.OtherFolderId, "A-003", 2),
            (CreateSheetChange)plan.Changes[0]);
    }

    [Fact]
    public void NameIsTrimmed()
    {
        var plan = _planner.Plan(Request("  A-003  "), TestSnapshots.Create());

        Assert.Single(plan.Changes);
        Assert.Equal("A-003", ((CreateSheetChange)plan.Changes[0]).Name);
    }

    [Fact]
    public void EmptyNameIsRejected()
    {
        var plan = _planner.Plan(Request("  "), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "sheet.name_required");
    }

    [Fact]
    public void DuplicateNameIsRejectedCaseInsensitively()
    {
        var plan = _planner.Plan(Request("a-001"), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "sheet.duplicate_name");
    }

    [Fact]
    public void MissingDestinationIsRejected()
    {
        var plan = _planner.Plan(Request("A-003", Guid.NewGuid()), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "sheet.destination_missing");
    }

    private static CreateSheetRequest Request(string name, Guid? destination = null) =>
        new(42, 1, destination ?? TestSnapshots.RootFolderId, name);
}
