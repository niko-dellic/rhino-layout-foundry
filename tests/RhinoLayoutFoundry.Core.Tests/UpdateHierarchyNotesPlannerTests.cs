using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class UpdateHierarchyNotesPlannerTests
{
    [Fact]
    public void PlansFolderAndSheetNotesTogether()
    {
        var snapshot = TestSnapshots.Create();
        var targets = new OverviewNodeKey[]
        {
            new(OverviewNodeKind.Folder, TestSnapshots.ChildFolderId),
            new(OverviewNodeKind.Sheet, TestSnapshots.SheetOneId),
            new(OverviewNodeKind.Detail, TestSnapshots.DetailOneId),
        };

        var plan = new UpdateHierarchyNotesPlanner().Plan(new UpdateHierarchyNotesRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            targets,
            "Coordinate grids"), snapshot);

        var change = Assert.IsType<UpdateHierarchyNotesChange>(Assert.Single(plan.Changes));
        Assert.Equal("Coordinate grids", change.NewFolderNotes[TestSnapshots.ChildFolderId]);
        Assert.Equal("Coordinate grids", change.NewSheetNotes[TestSnapshots.SheetOneId]);
        Assert.DoesNotContain(TestSnapshots.DetailOneId, change.NewSheetNotes.Keys);
    }

    [Fact]
    public void RejectsSelectionsWithoutFoldersOrLayouts()
    {
        var snapshot = TestSnapshots.Create();

        var plan = new UpdateHierarchyNotesPlanner().Plan(new UpdateHierarchyNotesRequest(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            [new(OverviewNodeKind.Detail, TestSnapshots.DetailOneId)],
            "Ignored"), snapshot);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Diagnostics, item => item.Code == "notes.empty_selection");
    }
}
