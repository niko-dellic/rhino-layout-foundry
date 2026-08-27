using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class MoveSheetsPlannerTests
{
    private readonly MoveSheetsPlanner _planner = new();

    [Fact]
    public void SheetMovesToFolderAndAppendsAfterExistingSheets()
    {
        var snapshot = TestSnapshots.Create(sheetTwoFolderId: TestSnapshots.ChildFolderId);

        var plan = _planner.Plan(
            Request(TestSnapshots.OtherFolderId, TestSnapshots.SheetOneId),
            snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(
            new MoveSheetChange(
                TestSnapshots.SheetOneId,
                TestSnapshots.ChildFolderId,
                TestSnapshots.OtherFolderId,
                0),
            AssertChange(plan));
    }

    [Fact]
    public void MultipleSheetsPreserveRequestOrder()
    {
        var plan = _planner.Plan(
            new MoveSheetsRequest(
                42,
                1,
                TestSnapshots.RootFolderId,
                [TestSnapshots.SheetTwoId, TestSnapshots.SheetOneId]),
            TestSnapshots.Create());

        Assert.True(plan.CanApply);
        Assert.Equal(TestSnapshots.SheetTwoId, ((MoveSheetChange)plan.Changes[0]).PageViewId);
        Assert.Equal(TestSnapshots.SheetOneId, ((MoveSheetChange)plan.Changes[1]).PageViewId);
        Assert.Equal(0, ((MoveSheetChange)plan.Changes[0]).Order);
        Assert.Equal(1, ((MoveSheetChange)plan.Changes[1]).Order);
    }

    [Fact]
    public void DuplicateSheetIdsAreMovedOnce()
    {
        var plan = _planner.Plan(
            new MoveSheetsRequest(
                42,
                1,
                TestSnapshots.RootFolderId,
                [TestSnapshots.SheetOneId, TestSnapshots.SheetOneId]),
            TestSnapshots.Create());

        Assert.Single(plan.Changes);
    }

    [Fact]
    public void MissingDestinationIsRejected()
    {
        var plan = _planner.Plan(Request(Guid.NewGuid(), TestSnapshots.SheetOneId), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.destination_missing");
    }

    [Fact]
    public void MissingSheetIsRejected()
    {
        var plan = _planner.Plan(Request(TestSnapshots.RootFolderId, Guid.NewGuid()), TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.sheet_missing");
    }

    [Fact]
    public void NoOpMoveIsRejected()
    {
        var plan = _planner.Plan(
            Request(TestSnapshots.ChildFolderId, TestSnapshots.SheetOneId),
            TestSnapshots.Create());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "move.already_in_folder");
    }

    private static MoveSheetsRequest Request(Guid destinationId, Guid sheetId)
    {
        return new MoveSheetsRequest(42, 1, destinationId, [sheetId]);
    }

    private static MoveSheetChange AssertChange(OperationPlan plan)
    {
        Assert.Single(plan.Changes);
        return (MoveSheetChange)plan.Changes[0];
    }
}
