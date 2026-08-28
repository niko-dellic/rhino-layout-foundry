using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class HierarchySelectionOperationsTests
{
    [Fact]
    public void MultipleLayoutsProduceOneCompositeDeletePlan()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new DeleteHierarchySelectionPlanner().Plan(new DeleteHierarchySelectionRequest(
            42,
            1,
            [Sheet(TestSnapshots.SheetOneId), Sheet(TestSnapshots.SheetTwoId)]), snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(2, plan.Changes.Count);
        Assert.True(plan.Changes.All(change => change is DeleteSheetChange));
    }

    [Fact]
    public void MultipleFoldersProduceOneCompositeDuplicatePlan()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new DuplicateHierarchySelectionPlanner().Plan(new DuplicateHierarchySelectionRequest(
            42,
            1,
            [Folder(TestSnapshots.ChildFolderId), Folder(TestSnapshots.OtherFolderId)]), snapshot);

        Assert.True(plan.CanApply);
        Assert.Equal(2, plan.Changes.Count);
        Assert.True(plan.Changes.All(change => change is DuplicateFolderChange));
    }

    [Fact]
    public void MixedFolderAndStandaloneLayoutAreBothIncluded()
    {
        var snapshot = TestSnapshots.Create();
        var selection = HierarchySelectionResolver.Resolve(snapshot,
            [Folder(TestSnapshots.ChildFolderId), Sheet(TestSnapshots.SheetTwoId)]);
        var plan = new DeleteHierarchySelectionPlanner().Plan(new DeleteHierarchySelectionRequest(
            42,
            1,
            [Folder(TestSnapshots.ChildFolderId), Sheet(TestSnapshots.SheetTwoId)]), snapshot);

        Assert.Single(selection.FolderRootIds);
        Assert.Single(selection.StandaloneSheetPageViewIds);
        Assert.Single(selection.FolderSheetPageViewIds);
        Assert.True(plan.CanApply);
        Assert.Contains(plan.Changes, change => change is DeleteFolderChange);
        Assert.Contains(plan.Changes, change => change is DeleteSheetChange);
    }

    [Fact]
    public void LayoutInsideSelectedFolderIsNotProcessedTwice()
    {
        var snapshot = TestSnapshots.Create();
        var selection = HierarchySelectionResolver.Resolve(snapshot,
            [Folder(TestSnapshots.ChildFolderId), Sheet(TestSnapshots.SheetOneId)]);
        var duplicate = new DuplicateHierarchySelectionPlanner().Plan(
            new DuplicateHierarchySelectionRequest(
                42,
                1,
                [Folder(TestSnapshots.ChildFolderId), Sheet(TestSnapshots.SheetOneId)]),
            snapshot);

        Assert.Empty(selection.StandaloneSheetPageViewIds);
        Assert.Equal(TestSnapshots.SheetOneId, selection.FolderSheetPageViewIds.Single());
        Assert.Single(duplicate.Changes);
        Assert.True(duplicate.Changes[0] is DuplicateFolderChange);
    }

    [Fact]
    public void DetailSelectionDuplicatesItsContainingLayout()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new DuplicateHierarchySelectionPlanner().Plan(new DuplicateHierarchySelectionRequest(
            42,
            1,
            [new OverviewNodeKey(OverviewNodeKind.Detail, TestSnapshots.DetailOneId)]), snapshot);

        Assert.True(plan.CanApply);
        Assert.Single(plan.Changes);
        var change = (DuplicateSheetChange)plan.Changes[0];
        Assert.Equal(TestSnapshots.SheetOneId, change.PageViewId);
    }

    [Fact]
    public void MissingSelectionBlocksTheWholeBatch()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new DeleteHierarchySelectionPlanner().Plan(new DeleteHierarchySelectionRequest(
            42,
            1,
            [Sheet(Guid.NewGuid()), Sheet(TestSnapshots.SheetTwoId)]), snapshot);

        Assert.False(plan.CanApply);
        Assert.Empty(plan.Changes);
        Assert.Contains(plan.Diagnostics, diagnostic => diagnostic.Code == "selection.missing");
    }

    private static OverviewNodeKey Folder(Guid id) => new(OverviewNodeKind.Folder, id);
    private static OverviewNodeKey Sheet(Guid id) => new(OverviewNodeKind.Sheet, id);
}
