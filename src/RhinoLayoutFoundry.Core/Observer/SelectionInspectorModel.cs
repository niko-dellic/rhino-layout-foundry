using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Observer;

public sealed record SelectionInspectorModel(
    int SelectedFolderCount,
    int SelectedLayoutCount,
    int SelectedDetailCount,
    IReadOnlyList<Guid> AffectedLayoutIds,
    IReadOnlyList<Guid> AffectedDetailIds,
    OverviewNodeKey? RenameTarget,
    string RenameValue,
    bool PrintIsMixed,
    bool? PrintIncluded,
    bool PaperIsMixed,
    double? PaperWidth,
    double? PaperHeight,
    string PaperUnitSystem,
    bool TitleBlockIsMixed,
    Guid? TitleBlockSourceInstanceId,
    bool DisplayModeIsMixed,
    Guid? DisplayModeId,
    bool? TemplateRegistered,
    IReadOnlyList<OverviewNodeKey>? NotesTargets = null,
    bool NotesIsMixed = false,
    string NotesValue = "")
{
    public int AffectedLayoutCount => AffectedLayoutIds.Count;
    public int AffectedDetailCount => AffectedDetailIds.Count;
    public bool HasSelection => SelectedFolderCount + SelectedLayoutCount + SelectedDetailCount > 0;
    public IReadOnlyList<OverviewNodeKey> EditableNotesTargets => NotesTargets ?? [];

    public static SelectionInspectorModel Create(
        DocumentSnapshot snapshot,
        IEnumerable<OverviewNodeKey> selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);
        var keys = selection.Distinct().Where(key => Exists(snapshot, key)).ToArray();
        var layoutIds = BatchTargetResolver.ResolveSheetIds(snapshot, keys);
        var detailIds = BatchTargetResolver.ResolveDetailIds(snapshot, keys);
        var layouts = layoutIds.Select(id => snapshot.Sheets[id]).ToArray();
        var details = snapshot.Sheets.Values.SelectMany(sheet => sheet.Details)
            .Where(detail => detailIds.Contains(detail.DetailViewportId))
            .ToArray();

        OverviewNodeKey? renameTarget = null;
        var renameValue = string.Empty;
        if (keys.Length == 1 && keys[0].Kind == OverviewNodeKind.Sheet &&
            snapshot.Sheets.TryGetValue(keys[0].Id, out var renameSheet))
        {
            renameTarget = keys[0];
            renameValue = renameSheet.Name;
        }
        else if (keys.Length == 1 && keys[0].Kind == OverviewNodeKind.Folder &&
                 keys[0].Id != snapshot.RootFolderId &&
                 snapshot.Folders.TryGetValue(keys[0].Id, out var renameFolder))
        {
            renameTarget = keys[0];
            renameValue = renameFolder.Name;
        }

        var printValues = layouts.Select(sheet => sheet.IncludeInPrintAll).Distinct().ToArray();
        var paperValues = layouts.Select(sheet =>
                (sheet.PageWidth, sheet.PageHeight, Unit: sheet.PageUnitSystem ?? string.Empty))
            .Distinct().ToArray();
        var titleBlocks = layouts.Select(sheet => sheet.TitleBlockInstanceObjectId).Distinct().ToArray();
        var displayModes = details.Select(detail => detail.DisplayModeId).Distinct().ToArray();
        bool? templateRegistered = keys.Length == 1 && keys[0].Kind == OverviewNodeKind.Sheet
            ? snapshot.Templates.Any(template => template.SourcePageViewId == keys[0].Id)
            : null;
        var notesTargets = keys.Where(key => key.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet)
            .ToArray();
        var noteValues = notesTargets.Select(key => key.Kind == OverviewNodeKind.Folder
                ? snapshot.Folders[key.Id].Notes ?? string.Empty
                : snapshot.Sheets[key.Id].Notes ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new SelectionInspectorModel(
            keys.Count(key => key.Kind == OverviewNodeKind.Folder),
            keys.Count(key => key.Kind == OverviewNodeKind.Sheet),
            keys.Count(key => key.Kind == OverviewNodeKind.Detail),
            layoutIds,
            detailIds,
            renameTarget,
            renameValue,
            printValues.Length > 1,
            printValues.Length == 1 ? printValues[0] : null,
            paperValues.Length > 1,
            paperValues.Length == 1 ? paperValues[0].PageWidth : null,
            paperValues.Length == 1 ? paperValues[0].PageHeight : null,
            paperValues.Length == 1 ? paperValues[0].Unit : string.Empty,
            titleBlocks.Length > 1,
            titleBlocks.Length == 1 ? titleBlocks[0] : null,
            displayModes.Length > 1,
            displayModes.Length == 1 ? displayModes[0] : null,
            templateRegistered,
            notesTargets,
            noteValues.Length > 1,
            noteValues.Length == 1 ? noteValues[0] : string.Empty);
    }

    private static bool Exists(DocumentSnapshot snapshot, OverviewNodeKey key) => key.Kind switch
    {
        OverviewNodeKind.Folder => snapshot.Folders.ContainsKey(key.Id),
        OverviewNodeKind.Sheet => snapshot.Sheets.ContainsKey(key.Id),
        OverviewNodeKind.Detail => snapshot.Sheets.Values.Any(sheet => sheet.DetailIds.Contains(key.Id)),
        _ => false,
    };
}
