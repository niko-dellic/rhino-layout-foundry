using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class MoveHierarchySelectionPlannerTests
{
    [Fact]
    public void MixedFolderAndSheetMoveIsOneCompositePlan()
    {
        var snapshot = TestSnapshots.Create(TestSnapshots.RootFolderId);
        var selectedFolder = snapshot.Folders.Values.First(folder => folder.Id != snapshot.RootFolderId);
        var selectedSheet = snapshot.Sheets.Values.First(sheet => sheet.FolderId != selectedFolder.Id);

        var plan = new MoveHierarchySelectionPlanner().Plan(
            new MoveHierarchySelectionRequest(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                TestSnapshots.OtherFolderId, [selectedFolder.Id], [selectedSheet.PageViewId]), snapshot);

        Assert.True(plan.CanApply);
        Assert.Contains(plan.Changes, change => change is MoveFolderChange);
        Assert.Contains(plan.Changes, change => change is MoveSheetChange);
    }

    [Fact]
    public void SheetCoveredBySelectedFolderIsNotMovedTwice()
    {
        var snapshot = TestSnapshots.Create();
        var selectedFolder = snapshot.Folders.Values.First(folder => folder.Id != snapshot.RootFolderId);
        var coveredSheet = snapshot.Sheets.Values.First(sheet => sheet.FolderId == selectedFolder.Id);

        var plan = new MoveHierarchySelectionPlanner().Plan(
            new MoveHierarchySelectionRequest(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                TestSnapshots.OtherFolderId, [selectedFolder.Id], [coveredSheet.PageViewId]), snapshot);

        Assert.True(plan.CanApply);
        Assert.Single(plan.Changes);
        Assert.IsType<MoveFolderChange>(plan.Changes[0]);
    }
}
