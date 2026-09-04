using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class AssignNamedViewPlannerTests
{
    [Fact]
    public void ExistingNamedViewCanTargetMultipleDetailsAtomically()
    {
        var snapshot = Snapshot();
        var details = snapshot.Sheets.Values.Single().DetailIds;

        var plan = new AssignNamedViewPlanner().Plan(
            new AssignNamedViewRequest(12, 7, details, "Level 02"),
            snapshot);

        Assert.True(plan.CanApply);
        var change = Assert.IsType<AssignNamedViewToDetailsChange>(plan.Changes.Single());
        Assert.Equal(details, change.DetailViewportIds);
        Assert.Equal("Level 02", change.NamedViewName);
    }

    [Fact]
    public void MissingNamedViewOrDetailBlocksWholeAssignment()
    {
        var snapshot = Snapshot();

        var plan = new AssignNamedViewPlanner().Plan(
            new AssignNamedViewRequest(12, 7, [Guid.NewGuid()], "Deleted view"),
            snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "named_view.missing");
        Assert.Contains(plan.Diagnostics, item => item.Code == "named_view.detail_missing");
    }

    private static DocumentSnapshot Snapshot()
    {
        var root = Guid.NewGuid();
        var sheetId = Guid.NewGuid();
        return new DocumentSnapshot(
            DocumentRuntimeSerialNumber: 12,
            Revision: 7,
            RootFolderId: root,
            Folders: new Dictionary<Guid, FolderRecord> { [root] = new(root, null, "Root", 0) },
            Sheets: new Dictionary<Guid, SheetSnapshot>
{
    [sheetId] = new(sheetId, root, 0, "Plan", [Guid.NewGuid(), Guid.NewGuid()],
                    new Dictionary<string, string>()),
},
            ExistingObjectIds: new HashSet<Guid>(),
            DisplayModeIds: new HashSet<Guid>())
        {
            NamedViews = new HashSet<string>(["Level 02"], StringComparer.OrdinalIgnoreCase)
        };
    }
}
