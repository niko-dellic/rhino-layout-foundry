using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Tests;

public sealed class SelectionInspectorModelTests
{
    [Fact]
    public void FolderSelectionResolvesAllDescendantsAndDeduplicatesExplicitChildren()
    {
        var snapshot = Snapshot(out var root, out var folder, out var nested, out var sheets, out var details);

        var model = SelectionInspectorModel.Create(snapshot,
        [
            new(OverviewNodeKind.Folder, folder),
            new(OverviewNodeKind.Folder, nested),
            new(OverviewNodeKind.Sheet, sheets[1]),
            new(OverviewNodeKind.Detail, details[1]),
        ]);

        Assert.Equal(2, model.AffectedLayoutCount);
        Assert.Equal(2, model.AffectedDetailCount);
        Assert.Equal(sheets.OrderBy(id => id), model.AffectedLayoutIds.OrderBy(id => id));
        Assert.DoesNotContain(root, model.AffectedLayoutIds);
    }

    [Fact]
    public void MixedPropertiesAreReportedWithoutInventingValues()
    {
        var snapshot = Snapshot(out _, out var folder, out _, out _, out _);

        var model = SelectionInspectorModel.Create(snapshot,
            [new(OverviewNodeKind.Folder, folder)]);

        Assert.True(model.PrintIsMixed);
        Assert.Null(model.PrintIncluded);
        Assert.True(model.PaperIsMixed);
        Assert.True(model.DisplayModeIsMixed);
        Assert.Equal(folder, model.RenameTarget!.Value.Id);
    }

    [Fact]
    public void OnlySingleNonRootFolderOrLayoutCanBeRenamed()
    {
        var snapshot = Snapshot(out var root, out var folder, out _, out var sheets, out _);

        var rootModel = SelectionInspectorModel.Create(snapshot,
            [new(OverviewNodeKind.Folder, root)]);
        var folderModel = SelectionInspectorModel.Create(snapshot,
            [new(OverviewNodeKind.Folder, folder)]);
        var sheetModel = SelectionInspectorModel.Create(snapshot,
            [new(OverviewNodeKind.Sheet, sheets[0])]);

        Assert.Null(rootModel.RenameTarget);
        Assert.Equal(folder, folderModel.RenameTarget!.Value.Id);
        Assert.Equal("Page 1", sheetModel.RenameValue);
    }

    [Fact]
    public void NotesUseOnlyDirectFolderAndLayoutSelectionsAndReportMixedValues()
    {
        var snapshot = Snapshot(out _, out var folder, out _, out var sheets, out var details);
        snapshot = snapshot with
        {
            Folders = snapshot.Folders.ToDictionary(pair => pair.Key, pair =>
                pair.Key == folder ? pair.Value with { Notes = "Folder note" } : pair.Value),
            Sheets = snapshot.Sheets.ToDictionary(pair => pair.Key, pair =>
                pair.Key == sheets[0] ? pair.Value with { Notes = "Sheet note" } : pair.Value),
        };

        var model = SelectionInspectorModel.Create(snapshot,
        [
            new(OverviewNodeKind.Folder, folder),
            new(OverviewNodeKind.Sheet, sheets[0]),
            new(OverviewNodeKind.Detail, details[0]),
        ]);

        Assert.True(model.NotesIsMixed);
        Assert.Equal(2, model.EditableNotesTargets.Count);
        Assert.DoesNotContain(model.EditableNotesTargets, target => target.Kind == OverviewNodeKind.Detail);
    }

    private static DocumentSnapshot Snapshot(
        out Guid root,
        out Guid folder,
        out Guid nested,
        out Guid[] sheets,
        out Guid[] details)
    {
        root = Guid.Parse("91000000-0000-0000-0000-000000000001");
        folder = Guid.Parse("91000000-0000-0000-0000-000000000002");
        nested = Guid.Parse("91000000-0000-0000-0000-000000000003");
        sheets =
        [
            Guid.Parse("92000000-0000-0000-0000-000000000001"),
            Guid.Parse("92000000-0000-0000-0000-000000000002"),
        ];
        details =
        [
            Guid.Parse("93000000-0000-0000-0000-000000000001"),
            Guid.Parse("93000000-0000-0000-0000-000000000002"),
        ];
        var modeA = Guid.Parse("94000000-0000-0000-0000-000000000001");
        var modeB = Guid.Parse("94000000-0000-0000-0000-000000000002");
        return new DocumentSnapshot(
            DocumentRuntimeSerialNumber: 19, Revision: 8, RootFolderId: root,
            Folders: new Dictionary<Guid, FolderRecord>
{
    [root] = new(root, null, "Root", 0),
    [folder] = new(folder, root, "Plans", 0),
    [nested] = new(nested, folder, "Nested", 0),
},
            Sheets: new Dictionary<Guid, SheetSnapshot>
{
    [sheets[0]] = new(sheets[0], folder, 0, "Page 1", [details[0]],
                    new Dictionary<string, string>(), 594, 420, "Millimeters",
                    [new DetailSnapshot(details[0], "Top", modeA, "Wireframe")],
                    IncludeInPrintAll: true),
    [sheets[1]] = new(sheets[1], nested, 0, "Page 2", [details[1]],
                    new Dictionary<string, string>(), 11, 17, "Inches",
                    [new DetailSnapshot(details[1], "Section", modeB, "Rendered")],
                    IncludeInPrintAll: false),
},
                        ExistingObjectIds: new HashSet<Guid>(), DisplayModeIds: new HashSet<Guid> { modeA, modeB })
        {
            DisplayModes = new Dictionary<Guid, string> { [modeA] = "Wireframe", [modeB] = "Rendered" }
        };
    }
}
