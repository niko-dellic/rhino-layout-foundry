using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class HierarchyPlacementPlannerTests
{
    [Fact]
    public void ReordersMultipleLayoutsBeforeSiblingAndPreservesRelativeOrder()
    {
        var snapshot = Snapshot(out var root, out _, out var sheets);
        var planner = new HierarchyPlacementPlanner();

        var plan = planner.Plan(new HierarchyPlacementRequest(17, 4,
            [], [sheets[2], sheets[0]],
            new(HierarchyPlacementKind.BeforeSibling, OverviewNodeKind.Sheet, sheets[1])), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<ReorganizeHierarchyChange>(Assert.Single(plan.Changes));
        var order = change.NewSheets.Where(item => item.FolderId == root)
            .OrderBy(item => item.Order).Select(item => item.PageViewId).ToArray();
        Assert.Equal([sheets[2], sheets[0], sheets[1]], order);
    }

    [Fact]
    public void LayoutCanBeInsertedAcrossFolders()
    {
        var snapshot = Snapshot(out _, out var folders, out var sheets);

        var plan = new HierarchyPlacementPlanner().Plan(new HierarchyPlacementRequest(17, 4,
            [], [sheets[0]],
            new(HierarchyPlacementKind.AfterSibling, OverviewNodeKind.Sheet, sheets[3])), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<ReorganizeHierarchyChange>(Assert.Single(plan.Changes));
        Assert.Contains(change.NewSheets,
            item => item.PageViewId == sheets[0] && item.FolderId == folders[0] && item.Order == 1);
    }

    [Fact]
    public void MixedSelectionCanMoveIntoFolderButNotBetweenRows()
    {
        var snapshot = Snapshot(out _, out var folders, out var sheets);
        var planner = new HierarchyPlacementPlanner();

        var into = planner.Plan(new HierarchyPlacementRequest(17, 4,
            [folders[0]], [sheets[1]],
            new(HierarchyPlacementKind.IntoFolder, OverviewNodeKind.Folder, folders[1])), snapshot);
        var between = planner.Plan(new HierarchyPlacementRequest(17, 4,
            [folders[0]], [sheets[1]],
            new(HierarchyPlacementKind.BeforeSibling, OverviewNodeKind.Folder, folders[1])), snapshot);

        Assert.True(into.CanApply);
        Assert.False(between.CanApply);
        Assert.Contains(between.Diagnostics, item => item.Code == "hierarchy.mixed_insertion");
    }

    [Fact]
    public void SelectedFolderCoversItsDescendantsAndTheirLayouts()
    {
        var snapshot = Snapshot(out _, out var folders, out var sheets);

        var plan = new HierarchyPlacementPlanner().Plan(new HierarchyPlacementRequest(17, 4,
            [folders[0], folders[2]], [sheets[3]],
            new(HierarchyPlacementKind.IntoFolder, OverviewNodeKind.Folder, folders[1])), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<ReorganizeHierarchyChange>(Assert.Single(plan.Changes));
        Assert.DoesNotContain(change.NewFolders, item => item.FolderId == folders[2]);
        Assert.DoesNotContain(change.NewSheets, item => item.PageViewId == sheets[3]);
    }

    [Fact]
    public void RejectsRootMovementCyclesAndStaleRevision()
    {
        var snapshot = Snapshot(out var root, out var folders, out _);
        var planner = new HierarchyPlacementPlanner();

        var rootMove = planner.Plan(new HierarchyPlacementRequest(17, 4,
            [root], [], new(HierarchyPlacementKind.IntoFolder, OverviewNodeKind.Folder, folders[0])), snapshot);
        var cycle = planner.Plan(new HierarchyPlacementRequest(17, 4,
            [folders[0]], [], new(HierarchyPlacementKind.IntoFolder, OverviewNodeKind.Folder, folders[2])), snapshot);
        var stale = planner.Plan(new HierarchyPlacementRequest(17, 3,
            [], [snapshot.Sheets.Keys.First()],
            new(HierarchyPlacementKind.IntoFolder, OverviewNodeKind.Folder, root)), snapshot);

        Assert.Contains(rootMove.Diagnostics, item => item.Code == "hierarchy.root_move");
        Assert.Contains(cycle.Diagnostics, item => item.Code == "hierarchy.folder_cycle");
        Assert.Contains(stale.Diagnostics, item => item.Code == "hierarchy.stale_revision");
    }

    private static DocumentSnapshot Snapshot(
        out Guid root,
        out Guid[] folders,
        out Guid[] sheets)
    {
        root = Guid.Parse("71000000-0000-0000-0000-000000000001");
        folders =
        [
            Guid.Parse("71000000-0000-0000-0000-000000000002"),
            Guid.Parse("71000000-0000-0000-0000-000000000003"),
            Guid.Parse("71000000-0000-0000-0000-000000000004"),
        ];
        sheets =
        [
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            Guid.Parse("72000000-0000-0000-0000-000000000002"),
            Guid.Parse("72000000-0000-0000-0000-000000000003"),
            Guid.Parse("72000000-0000-0000-0000-000000000004"),
        ];
        return new DocumentSnapshot(
            17, 4, root,
            new Dictionary<Guid, FolderRecord>
            {
                [root] = new(root, null, "Root", 0),
                [folders[0]] = new(folders[0], root, "Plans", 0),
                [folders[1]] = new(folders[1], root, "Details", 1),
                [folders[2]] = new(folders[2], folders[0], "Nested", 0),
            },
            new Dictionary<Guid, SheetSnapshot>
            {
                [sheets[0]] = Sheet(sheets[0], root, 0, "Page 1"),
                [sheets[1]] = Sheet(sheets[1], root, 1, "Page 2"),
                [sheets[2]] = Sheet(sheets[2], root, 2, "Page 3"),
                [sheets[3]] = Sheet(sheets[3], folders[0], 0, "Page 4"),
            },
            new HashSet<Guid>(), new HashSet<Guid>());
    }

    private static SheetSnapshot Sheet(Guid id, Guid folder, int order, string name) =>
        new(id, folder, order, name, [], new Dictionary<string, string>());
}
