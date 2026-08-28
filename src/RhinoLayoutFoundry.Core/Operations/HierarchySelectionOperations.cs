using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record HierarchySelection(
    IReadOnlyList<Guid> FolderRootIds,
    IReadOnlySet<Guid> ExpandedFolderIds,
    IReadOnlyList<Guid> StandaloneSheetPageViewIds,
    IReadOnlyList<Guid> FolderSheetPageViewIds,
    IReadOnlyList<OverviewNodeKey> UnresolvedKeys)
{
    public IReadOnlyList<Guid> AllSheetPageViewIds =>
        StandaloneSheetPageViewIds.Concat(FolderSheetPageViewIds).Distinct().ToArray();

    public int SelectedItemCount => FolderRootIds.Count + StandaloneSheetPageViewIds.Count;
}

public static class HierarchySelectionResolver
{
    public static HierarchySelection Resolve(
        DocumentSnapshot snapshot,
        IEnumerable<OverviewNodeKey> selection)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);

        var keys = selection.Distinct().ToArray();
        var unresolved = new List<OverviewNodeKey>();
        var selectedFolders = new HashSet<Guid>();
        var selectedSheets = new HashSet<Guid>();
        var detailOwners = snapshot.Sheets.Values
            .SelectMany(sheet => sheet.DetailIds.Select(detailId => (detailId, sheet.PageViewId)))
            .ToDictionary(pair => pair.detailId, pair => pair.PageViewId);

        foreach (var key in keys)
        {
            switch (key.Kind)
            {
                case OverviewNodeKind.Folder when key.Id != snapshot.RootFolderId && snapshot.Folders.ContainsKey(key.Id):
                    selectedFolders.Add(key.Id);
                    break;
                case OverviewNodeKind.Sheet when snapshot.Sheets.ContainsKey(key.Id):
                    selectedSheets.Add(key.Id);
                    break;
                case OverviewNodeKind.Detail when detailOwners.TryGetValue(key.Id, out var sheetId):
                    selectedSheets.Add(sheetId);
                    break;
                default:
                    unresolved.Add(key);
                    break;
            }
        }

        var folderRoots = selectedFolders
            .Where(id => !HasSelectedAncestor(id, selectedFolders, snapshot.Folders))
            .OrderBy(id => snapshot.Folders[id].ParentId)
            .ThenBy(id => snapshot.Folders[id].Order)
            .ToArray();
        var expandedFolders = folderRoots
            .SelectMany(id => Descendants(id, snapshot.Folders))
            .ToHashSet();
        var folderSheets = snapshot.Sheets.Values
            .Where(sheet => expandedFolders.Contains(sheet.FolderId))
            .OrderBy(sheet => sheet.FolderId)
            .ThenBy(sheet => sheet.Order)
            .Select(sheet => sheet.PageViewId)
            .ToArray();
        var standaloneSheets = selectedSheets
            .Where(id => !expandedFolders.Contains(snapshot.Sheets[id].FolderId))
            .OrderBy(id => snapshot.Sheets[id].FolderId)
            .ThenBy(id => snapshot.Sheets[id].Order)
            .ToArray();

        return new HierarchySelection(
            folderRoots,
            expandedFolders,
            standaloneSheets,
            folderSheets,
            unresolved);
    }

    private static bool HasSelectedAncestor(
        Guid folderId,
        IReadOnlySet<Guid> selectedFolders,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var current = folders[folderId];
        while (current.ParentId is { } parentId && folders.TryGetValue(parentId, out current))
        {
            if (selectedFolders.Contains(parentId)) return true;
        }
        return false;
    }

    private static IEnumerable<Guid> Descendants(
        Guid rootId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Values.Where(folder =>
                         folder.ParentId is { } parent && result.Contains(parent)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }
}

public sealed record DeleteHierarchySelectionRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<OverviewNodeKey> Selection);

public sealed class DeleteHierarchySelectionPlanner : IOperationPlanner<DeleteHierarchySelectionRequest>
{
    public OperationPlan Plan(DeleteHierarchySelectionRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = ValidateContext(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        var resolved = HierarchySelectionResolver.Resolve(snapshot, request.Selection);
        AddSelectionDiagnostics(resolved, diagnostics);
        var changes = new List<OperationChange>();

        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            foreach (var folderId in resolved.FolderRootIds)
            {
                var folder = snapshot.Folders[folderId];
                var folderIds = Descendants(folderId, snapshot.Folders);
                var sheets = snapshot.Sheets.Values
                    .Where(sheet => folderIds.Contains(sheet.FolderId))
                    .OrderBy(sheet => sheet.Order)
                    .Select(sheet => sheet.PageViewId)
                    .ToArray();
                changes.Add(new DeleteFolderChange(
                    folder.Id,
                    folder.ParentId!.Value,
                    folder.Name,
                    folderIds.Where(id => id != folder.Id).ToArray(),
                    sheets));
            }

            foreach (var sheetId in resolved.StandaloneSheetPageViewIds)
            {
                var sheet = snapshot.Sheets[sheetId];
                changes.Add(new DeleteSheetChange(sheet.PageViewId, sheet.FolderId, sheet.Name));
            }

            if (resolved.AllSheetPageViewIds.Count > 0)
                diagnostics.Add(new Diagnostic(
                    "selection.delete_undo_unavailable",
                    DiagnosticSeverity.Warning,
                    "Rhino layout deletion cannot be undone. Foundry validates the complete selection before deleting it."));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Delete {resolved.SelectedItemCount} selected item{(resolved.SelectedItemCount == 1 ? string.Empty : "s")}",
            changes,
            diagnostics);
    }

    private static HashSet<Guid> Descendants(Guid rootId, IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Values.Where(folder =>
                         folder.ParentId is { } parent && result.Contains(parent)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }

    private static List<Diagnostic> ValidateContext(uint serial, long revision, DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (serial != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("selection.document_mismatch", "The active Rhino document changed."));
        if (revision != snapshot.Revision)
            diagnostics.Add(Error("selection.stale_revision", "The Rhino document changed. Refresh and try again."));
        return diagnostics;
    }

    private static void AddSelectionDiagnostics(HierarchySelection selection, ICollection<Diagnostic> diagnostics)
    {
        if (selection.UnresolvedKeys.Count > 0)
            diagnostics.Add(Error("selection.missing", "One or more selected items no longer exist."));
        if (selection.SelectedItemCount == 0)
            diagnostics.Add(Error("selection.empty", "Select at least one folder, layout, or detail."));
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record DuplicateHierarchySelectionRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<OverviewNodeKey> Selection);

public sealed class DuplicateHierarchySelectionPlanner : IOperationPlanner<DuplicateHierarchySelectionRequest>
{
    public OperationPlan Plan(DuplicateHierarchySelectionRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("selection.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("selection.stale_revision", "The Rhino document changed. Refresh and try again."));
        var resolved = HierarchySelectionResolver.Resolve(snapshot, request.Selection);
        if (resolved.UnresolvedKeys.Count > 0)
            diagnostics.Add(Error("selection.missing", "One or more selected items no longer exist."));
        if (resolved.SelectedItemCount == 0)
            diagnostics.Add(Error("selection.empty", "Select at least one folder, layout, or detail."));
        var changes = new List<OperationChange>();

        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            foreach (var folderId in resolved.FolderRootIds)
            {
                var folder = snapshot.Folders[folderId];
                var folderPlan = new DuplicateFolderPlanner().Plan(new DuplicateFolderRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    folder.Id,
                    folder.Name), snapshot);
                changes.AddRange(folderPlan.Changes);
            }

            foreach (var sheetId in resolved.StandaloneSheetPageViewIds)
            {
                var sheet = snapshot.Sheets[sheetId];
                changes.Add(new DuplicateSheetChange(
                    sheet.PageViewId,
                    sheet.FolderId,
                    sheet.FolderId,
                    sheet.Name));
            }

            if (resolved.AllSheetPageViewIds.Count > 0)
                diagnostics.Add(new Diagnostic(
                    "selection.duplicate_undo_unavailable",
                    DiagnosticSeverity.Warning,
                    "Rhino does not expose native Undo for duplicated layouts. Foundry removes every incomplete copy if the batch fails."));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Duplicate {resolved.SelectedItemCount} selected item{(resolved.SelectedItemCount == 1 ? string.Empty : "s")}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record PasteHierarchySelectionRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    IReadOnlyList<OverviewNodeKey> Selection,
    ObserverPointRecord? CanvasTargetOrigin = null);

/// <summary>
/// Copies one normalized hierarchy selection into one destination folder. Unlike
/// DuplicateHierarchySelectionPlanner, every top-level source is appended to the
/// same destination.
/// </summary>
public sealed class PasteHierarchySelectionPlanner : IOperationPlanner<PasteHierarchySelectionRequest>
{
    public OperationPlan Plan(PasteHierarchySelectionRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("paste.document_mismatch", "The copied items belong to a different Rhino document."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("paste.stale_revision", "The Rhino document changed before Paste. Refresh and try again."));
        if (!snapshot.Folders.ContainsKey(request.DestinationFolderId))
            diagnostics.Add(Error("paste.destination_missing", "The paste destination no longer exists."));
        if (request.CanvasTargetOrigin is { } point &&
            (!double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            diagnostics.Add(Error("paste.canvas_target_invalid", "The Canvas paste location is invalid."));

        var resolved = HierarchySelectionResolver.Resolve(snapshot, request.Selection);
        if (resolved.UnresolvedKeys.Count > 0)
            diagnostics.Add(Error("paste.source_missing", "One or more copied items no longer exist."));
        if (resolved.SelectedItemCount == 0)
            diagnostics.Add(Error("paste.empty", "Copy at least one folder or layout before pasting."));

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var reservedNames = snapshot.Folders.Values
                .Where(folder => folder.ParentId == request.DestinationFolderId)
                .Select(folder => folder.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var folderId in resolved.FolderRootIds)
            {
                var source = snapshot.Folders[folderId];
                var descendants = Descendants(source.Id, snapshot.Folders);
                var map = descendants.ToDictionary(id => id, _ => Guid.NewGuid());
                var name = ReserveCopyName(source.Name, reservedNames);
                changes.Add(new DuplicateFolderChange(
                    source.Id,
                    source.ParentId ?? snapshot.RootFolderId,
                    request.DestinationFolderId,
                    source.Name,
                    name,
                    map));
            }

            foreach (var sheetId in resolved.StandaloneSheetPageViewIds)
            {
                var sheet = snapshot.Sheets[sheetId];
                changes.Add(new DuplicateSheetChange(
                    sheet.PageViewId,
                    sheet.FolderId,
                    request.DestinationFolderId,
                    sheet.Name));
            }

            if (request.CanvasTargetOrigin is { } target)
                changes.Add(new PlacePastedHierarchyOnCanvasChange(target));

            if (resolved.AllSheetPageViewIds.Count > 0)
                diagnostics.Add(new Diagnostic(
                    "paste.undo_unavailable",
                    DiagnosticSeverity.Warning,
                    "Rhino does not expose native Undo for duplicated layouts. Foundry removes every incomplete copy if the paste fails."));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Paste {resolved.SelectedItemCount} copied item{(resolved.SelectedItemCount == 1 ? string.Empty : "s")}",
            changes,
            diagnostics);
    }

    private static string ReserveCopyName(string sourceName, ISet<string> reservedNames)
    {
        var candidate = $"{sourceName} copy";
        if (reservedNames.Add(candidate)) return candidate;
        for (var index = 2; ; index++)
        {
            candidate = $"{sourceName} copy {index}";
            if (reservedNames.Add(candidate)) return candidate;
        }
    }

    private static HashSet<Guid> Descendants(
        Guid rootId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Values.Where(folder =>
                         folder.ParentId is { } parent && result.Contains(parent)))
                changed |= result.Add(folder.Id);
        }
        return result;
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record HierarchyPasteDestination(
    bool Succeeded,
    Guid? FolderId,
    string Message)
{
    public static HierarchyPasteDestination Resolve(
        DocumentSnapshot snapshot,
        IReadOnlyCollection<OverviewNodeKey> selection,
        OverviewNodeKey? anchor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Count == 0)
            return new HierarchyPasteDestination(true, snapshot.RootFolderId, string.Empty);

        var target = anchor is { } anchored && selection.Contains(anchored)
            ? anchored
            : selection.Count == 1
                ? selection.Single()
                : (OverviewNodeKey?)null;
        if (target is null)
            return new HierarchyPasteDestination(false, null,
                "Choose one focused folder or layout as the paste destination.");

        if (target.Value.Kind == OverviewNodeKind.Folder && snapshot.Folders.ContainsKey(target.Value.Id))
            return new HierarchyPasteDestination(true, target.Value.Id, string.Empty);
        if (target.Value.Kind == OverviewNodeKind.Sheet &&
            snapshot.Sheets.TryGetValue(target.Value.Id, out var sheet))
            return new HierarchyPasteDestination(true, sheet.FolderId, string.Empty);
        if (target.Value.Kind == OverviewNodeKind.Detail)
        {
            var owner = snapshot.Sheets.Values.FirstOrDefault(candidate =>
                candidate.DetailIds.Contains(target.Value.Id));
            if (owner is not null)
                return new HierarchyPasteDestination(true, owner.FolderId, string.Empty);
        }

        return new HierarchyPasteDestination(false, null,
            "The focused paste destination no longer exists.");
    }
}
