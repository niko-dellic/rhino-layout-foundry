using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class InlinePropertyPlannerTests
{
    [Fact]
    public void FolderDisplayTargetResolvesEveryDescendantDetail()
    {
        var snapshot = TestSnapshots.Create(TestSnapshots.ChildFolderId);

        var result = BatchTargetResolver.ResolveDetailIds(snapshot,
            [new OverviewNodeKey(OverviewNodeKind.Folder, TestSnapshots.ChildFolderId)]);

        Assert.Equal([TestSnapshots.DetailOneId, TestSnapshots.DetailTwoId], result.OrderBy(id => id));
    }

    [Fact]
    public void DetailDisplayPlannerTargetsOnlyTheRequestedViewport()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new UpdateDetailDisplayModesPlanner().Plan(
            new UpdateDetailDisplayModesRequest(42, 1,
                [TestSnapshots.DetailOneId], TestSnapshots.DisplayModeTwoId),
            snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<UpdateDetailDisplayModesChange>(Assert.Single(plan.Changes));
        Assert.Equal([TestSnapshots.DetailOneId], change.DetailViewportIds);
    }

    [Fact]
    public void PrintInclusionCapturesExpectedBeforeValues()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new SetPrintInclusionPlanner().Plan(
            new SetPrintInclusionRequest(42, 1,
                [TestSnapshots.SheetOneId, TestSnapshots.SheetTwoId], false),
            snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<SetPrintInclusionChange>(Assert.Single(plan.Changes));
        Assert.All(change.ExpectedValues.Values, Assert.True);
        Assert.False(change.IncludeInPrintAll);
    }

    [Fact]
    public void MissingDisplayModeBlocksInlineEdit()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new UpdateDetailDisplayModesPlanner().Plan(
            new UpdateDetailDisplayModesRequest(42, 1,
                [TestSnapshots.DetailOneId], Guid.NewGuid()),
            snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "inline.display_mode_missing");
    }
}
