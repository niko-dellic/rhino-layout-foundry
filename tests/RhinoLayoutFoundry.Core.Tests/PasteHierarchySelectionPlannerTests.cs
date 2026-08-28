using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class PasteHierarchySelectionPlannerTests
{
    [Fact]
    public void DetailPastesItsContainingLayoutIntoDestination()
    {
        var snapshot = TestSnapshots.Create();
        var plan = Plan(snapshot, TestSnapshots.OtherFolderId,
            [new OverviewNodeKey(OverviewNodeKind.Detail, TestSnapshots.DetailOneId)]);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<DuplicateSheetChange>(plan.Changes.Single());
        Assert.Equal(TestSnapshots.SheetOneId, change.PageViewId);
        Assert.Equal(TestSnapshots.ChildFolderId, change.ExpectedFolderId);
        Assert.Equal(TestSnapshots.OtherFolderId, change.DestinationFolderId);
    }

    [Fact]
    public void FolderSubtreeTargetsDifferentParent()
    {
        var nestedId = Guid.NewGuid();
        var snapshot = TestSnapshots.Create() with
        {
            Folders = new Dictionary<Guid, FolderRecord>(TestSnapshots.Create().Folders)
            {
                [nestedId] = new(nestedId, TestSnapshots.ChildFolderId, "Nested", 0),
            },
        };
        var plan = Plan(snapshot, TestSnapshots.OtherFolderId,
            [Folder(TestSnapshots.ChildFolderId)]);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<DuplicateFolderChange>(plan.Changes.Single());
        Assert.Equal(TestSnapshots.RootFolderId, change.ExpectedParentFolderId);
        Assert.Equal(TestSnapshots.OtherFolderId, change.DestinationParentFolderId);
        Assert.Equal(2, change.FolderIdMap.Count);
    }

    [Fact]
    public void FolderAndCoveredLayoutAreCopiedOnlyOnce()
    {
        var snapshot = TestSnapshots.Create();
        var plan = Plan(snapshot, TestSnapshots.OtherFolderId,
            [Folder(TestSnapshots.ChildFolderId), Sheet(TestSnapshots.SheetOneId)]);

        Assert.True(plan.CanApply);
        Assert.Single(plan.Changes);
        Assert.IsType<DuplicateFolderChange>(plan.Changes[0]);
    }

    [Fact]
    public void BatchFolderNamesAreReservedAgainstDestinationAndEachOther()
    {
        var secondPlansId = Guid.NewGuid();
        var snapshot = TestSnapshots.Create() with
        {
            Folders = new Dictionary<Guid, FolderRecord>(TestSnapshots.Create().Folders)
            {
                [secondPlansId] = new(secondPlansId, TestSnapshots.OtherFolderId, "Plans", 0),
            },
        };
        var plan = Plan(snapshot, TestSnapshots.RootFolderId,
            [Folder(TestSnapshots.ChildFolderId), Folder(secondPlansId)]);

        var names = plan.Changes.OfType<DuplicateFolderChange>().Select(change => change.NewName).ToArray();
        Assert.Equal(["Plans copy", "Plans copy 2"], names);
    }

    [Fact]
    public void CanvasTargetAddsPlacementChange()
    {
        var snapshot = TestSnapshots.Create();
        var plan = new PasteHierarchySelectionPlanner().Plan(new PasteHierarchySelectionRequest(
            42,
            1,
            TestSnapshots.RootFolderId,
            [Sheet(TestSnapshots.SheetOneId)],
            new ObserverPointRecord(125, -40)), snapshot);

        Assert.True(plan.CanApply);
        var placement = Assert.Single(plan.Changes.OfType<PlacePastedHierarchyOnCanvasChange>());
        Assert.Equal(new ObserverPointRecord(125, -40), placement.TargetOrigin);
    }

    [Fact]
    public void DifferentDocumentAndMissingSourceBlockPaste()
    {
        var snapshot = TestSnapshots.Create();
        var wrongDocument = new PasteHierarchySelectionPlanner().Plan(new PasteHierarchySelectionRequest(
            99, 1, TestSnapshots.RootFolderId, [Sheet(TestSnapshots.SheetOneId)]), snapshot);
        var missing = Plan(snapshot, TestSnapshots.RootFolderId, [Sheet(Guid.NewGuid())]);

        Assert.False(wrongDocument.CanApply);
        Assert.Contains(wrongDocument.Diagnostics, diagnostic => diagnostic.Code == "paste.document_mismatch");
        Assert.False(missing.CanApply);
        Assert.Contains(missing.Diagnostics, diagnostic => diagnostic.Code == "paste.source_missing");
    }

    [Fact]
    public void DestinationResolverUsesFolderSheetDetailAndRoot()
    {
        var snapshot = TestSnapshots.Create();
        var folder = Folder(TestSnapshots.ChildFolderId);
        var sheet = Sheet(TestSnapshots.SheetTwoId);
        var detail = new OverviewNodeKey(OverviewNodeKind.Detail, TestSnapshots.DetailOneId);

        Assert.Equal(TestSnapshots.ChildFolderId,
            HierarchyPasteDestination.Resolve(snapshot, [folder], folder).FolderId);
        Assert.Equal(TestSnapshots.OtherFolderId,
            HierarchyPasteDestination.Resolve(snapshot, [sheet], sheet).FolderId);
        Assert.Equal(TestSnapshots.ChildFolderId,
            HierarchyPasteDestination.Resolve(snapshot, [detail], detail).FolderId);
        Assert.Equal(TestSnapshots.RootFolderId,
            HierarchyPasteDestination.Resolve(snapshot, [], null).FolderId);
    }

    [Fact]
    public void DestinationResolverRequiresAnchorForAmbiguousSelection()
    {
        var snapshot = TestSnapshots.Create();
        var selection = new[] { Sheet(TestSnapshots.SheetOneId), Sheet(TestSnapshots.SheetTwoId) };

        var ambiguous = HierarchyPasteDestination.Resolve(snapshot, selection, null);
        var anchored = HierarchyPasteDestination.Resolve(snapshot, selection, selection[1]);

        Assert.False(ambiguous.Succeeded);
        Assert.Equal(TestSnapshots.OtherFolderId, anchored.FolderId);
    }

    private static OperationPlan Plan(
        DocumentSnapshot snapshot,
        Guid destinationFolderId,
        IReadOnlyList<OverviewNodeKey> selection) =>
        new PasteHierarchySelectionPlanner().Plan(new PasteHierarchySelectionRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            destinationFolderId,
            selection), snapshot);

    private static OverviewNodeKey Folder(Guid id) => new(OverviewNodeKind.Folder, id);
    private static OverviewNodeKey Sheet(Guid id) => new(OverviewNodeKind.Sheet, id);
}
