using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class ReorderSheetsPlannerTests
{
    [Fact]
    public void SheetCanMoveBeforeSiblingWithoutChangingFolder()
    {
        var snapshot = Snapshot(out var first, out _, out var third);

        var plan = new ReorderSheetsPlanner().Plan(
            new ReorderSheetsRequest(9, 3, third, first),
            snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<ReorderSheetsChange>(plan.Changes.Single());
        Assert.Equal(snapshot.Sheets[first].FolderId, change.FolderId);
        Assert.Equal(0, change.NewOrders[third]);
        Assert.Equal(1, change.NewOrders[first]);
    }

    [Fact]
    public void CrossFolderReorderRequiresHierarchyMoveFirst()
    {
        var snapshot = Snapshot(out var first, out _, out var third);
        var secondFolder = Guid.NewGuid();
        snapshot = snapshot with
        {
            Folders = snapshot.Folders.Append(
                new KeyValuePair<Guid, FolderRecord>(secondFolder, new(secondFolder, snapshot.RootFolderId, "Other", 1)))
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            Sheets = snapshot.Sheets.ToDictionary(
                pair => pair.Key,
                pair => pair.Key == third ? pair.Value with { FolderId = secondFolder } : pair.Value),
        };

        var plan = new ReorderSheetsPlanner().Plan(
            new ReorderSheetsRequest(9, 3, third, first), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "reorder.different_folder");
    }

    [Fact]
    public void LayoutCanMoveToEndOfFolder()
    {
        var snapshot = TestSnapshots.Create(TestSnapshots.ChildFolderId);

        var plan = new ReorderSheetsPlanner().Plan(
            new ReorderSheetsRequest(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                TestSnapshots.SheetOneId, null), snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<ReorderSheetsChange>(Assert.Single(plan.Changes));
        Assert.True(change.NewOrders[TestSnapshots.SheetOneId] > change.NewOrders[TestSnapshots.SheetTwoId]);
    }

    private static DocumentSnapshot Snapshot(out Guid first, out Guid second, out Guid third)
    {
        var root = Guid.NewGuid();
        first = Guid.NewGuid();
        second = Guid.NewGuid();
        third = Guid.NewGuid();
        return new DocumentSnapshot(
            DocumentRuntimeSerialNumber: 9,
            Revision: 3,
            RootFolderId: root,
            Folders: new Dictionary<Guid, FolderRecord> { [root] = new(root, null, "Root", 0) },
            Sheets: new Dictionary<Guid, SheetSnapshot>
{
    [first] = Sheet(first, root, 0, "Page 1"),
    [second] = Sheet(second, root, 1, "Page 2"),
    [third] = Sheet(third, root, 2, "Page 3"),
},
            ExistingObjectIds: new HashSet<Guid>(),
            DisplayModeIds: new HashSet<Guid>());
    }

    private static SheetSnapshot Sheet(Guid id, Guid folderId, int order, string name) =>
        new(id, folderId, order, name, [], new Dictionary<string, string>());
}
