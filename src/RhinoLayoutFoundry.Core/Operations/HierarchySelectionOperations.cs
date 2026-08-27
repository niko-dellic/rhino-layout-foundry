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
                changes.Add(new DuplicateSheetChange(sheet.PageViewId, sheet.FolderId, sheet.Name));
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
