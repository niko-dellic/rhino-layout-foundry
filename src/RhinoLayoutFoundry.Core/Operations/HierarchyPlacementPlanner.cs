using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Operations;

public enum HierarchyPlacementKind
{
    IntoFolder,
    BeforeSibling,
    AfterSibling,
}

public sealed record HierarchyPlacementTarget(
    HierarchyPlacementKind Kind,
    OverviewNodeKind TargetKind,
    Guid TargetId);

public sealed record HierarchyPlacementRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<Guid> FolderIds,
    IReadOnlyList<Guid> SheetIds,
    HierarchyPlacementTarget Target);

/// <summary>
/// Produces one before-value-checked hierarchy mutation. Folder and layout
/// orders are normalized independently so the folders-first presentation is
/// retained without coupling the two order spaces.
/// </summary>
public sealed class HierarchyPlacementPlanner : IOperationPlanner<HierarchyPlacementRequest>
{
    public OperationPlan Plan(HierarchyPlacementRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = ValidateEnvelope(request, snapshot);
        var folderIds = StableDistinct(request.FolderIds)
            .Where(id => id != Guid.Empty)
            .ToList();
        var sheetIds = StableDistinct(request.SheetIds)
            .Where(id => id != Guid.Empty)
            .ToList();

        foreach (var folderId in folderIds)
        {
            if (folderId == snapshot.RootFolderId)
                diagnostics.Add(Error("hierarchy.root_move", "The document root cannot be moved."));
            else if (!snapshot.Folders.ContainsKey(folderId))
                diagnostics.Add(Error("hierarchy.folder_missing", "A selected folder no longer exists.", folderId));
        }

        foreach (var sheetId in sheetIds.Where(id => !snapshot.Sheets.ContainsKey(id)))
            diagnostics.Add(Error("hierarchy.sheet_missing", "A selected layout no longer exists.", sheetId));

        folderIds = RemoveCoveredFolders(folderIds, snapshot);
        var coveredFolderIds = folderIds
            .SelectMany(id => DescendantsAndSelf(id, snapshot.Folders))
            .ToHashSet();
        sheetIds = sheetIds
            .Where(id => snapshot.Sheets.TryGetValue(id, out var sheet) &&
                         !coveredFolderIds.Contains(sheet.FolderId))
            .ToList();

        if (folderIds.Count == 0 && sheetIds.Count == 0)
            diagnostics.Add(Error("hierarchy.empty_selection", "Select a folder or layout to move."));

        if (request.Target.Kind != HierarchyPlacementKind.IntoFolder &&
            folderIds.Count > 0 && sheetIds.Count > 0)
            diagnostics.Add(Error("hierarchy.mixed_insertion",
                "Mixed folder and layout selections can only be moved into a folder or the document root."));

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            return Failed(snapshot, diagnostics);

        if (!TryResolveDestination(request.Target, folderIds, sheetIds, snapshot,
                diagnostics, out var destinationFolderId, out var referenceIndex))
            return Failed(snapshot, diagnostics);

        foreach (var folderId in folderIds)
        {
            if (folderId == destinationFolderId ||
                IsDescendant(destinationFolderId, folderId, snapshot.Folders))
                diagnostics.Add(Error("hierarchy.folder_cycle",
                    "A folder cannot be moved inside itself or one of its descendants.", folderId));
        }

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error))
            return Failed(snapshot, diagnostics);

        var folderOrders = BuildFolderOrders(snapshot);
        var sheetOrders = BuildSheetOrders(snapshot);
        RemoveSelected(folderOrders, folderIds);
        RemoveSelected(sheetOrders, sheetIds);

        if (folderIds.Count > 0)
        {
            var destination = folderOrders.GetValueOrDefault(destinationFolderId) ?? [];
            folderOrders[destinationFolderId] = destination;
            var insertAt = request.Target.Kind == HierarchyPlacementKind.IntoFolder
                ? destination.Count
                : Math.Clamp(referenceIndex, 0, destination.Count);
            destination.InsertRange(insertAt, folderIds);
        }

        if (sheetIds.Count > 0)
        {
            var destination = sheetOrders.GetValueOrDefault(destinationFolderId) ?? [];
            sheetOrders[destinationFolderId] = destination;
            var insertAt = request.Target.Kind == HierarchyPlacementKind.IntoFolder
                ? destination.Count
                : Math.Clamp(referenceIndex, 0, destination.Count);
            destination.InsertRange(insertAt, sheetIds);
        }

        var expectedFolders = new List<HierarchyFolderPlacement>();
        var nextFolders = new List<HierarchyFolderPlacement>();
        foreach (var pair in folderOrders)
        {
            for (var index = 0; index < pair.Value.Count; index++)
            {
                var current = snapshot.Folders[pair.Value[index]];
                if (current.ParentId == pair.Key && current.Order == index) continue;
                expectedFolders.Add(new(current.Id, current.ParentId, current.Order));
                nextFolders.Add(new(current.Id, pair.Key, index));
            }
        }

        var expectedSheets = new List<HierarchySheetPlacement>();
        var nextSheets = new List<HierarchySheetPlacement>();
        foreach (var pair in sheetOrders)
        {
            for (var index = 0; index < pair.Value.Count; index++)
            {
                var current = snapshot.Sheets[pair.Value[index]];
                if (current.FolderId == pair.Key && current.Order == index) continue;
                expectedSheets.Add(new(current.PageViewId, current.FolderId, current.Order));
                nextSheets.Add(new(current.PageViewId, pair.Key, index));
            }
        }

        if (expectedFolders.Count == 0 && expectedSheets.Count == 0)
        {
            diagnostics.Add(Error("hierarchy.no_change", "The selection is already at that position."));
            return Failed(snapshot, diagnostics);
        }

        var movedCount = folderIds.Count + sheetIds.Count;
        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Reorganize {movedCount} hierarchy item{(movedCount == 1 ? string.Empty : "s")}",
            [new ReorganizeHierarchyChange(expectedFolders, expectedSheets, nextFolders, nextSheets)],
            diagnostics);
    }

    private static List<Diagnostic> ValidateEnvelope(
        HierarchyPlacementRequest request,
        DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("hierarchy.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("hierarchy.stale_revision", "The Rhino document changed. Refresh and try again."));
        return diagnostics;
    }

    private static bool TryResolveDestination(
        HierarchyPlacementTarget target,
        IReadOnlyCollection<Guid> folderIds,
        IReadOnlyCollection<Guid> sheetIds,
        DocumentSnapshot snapshot,
        ICollection<Diagnostic> diagnostics,
        out Guid destinationFolderId,
        out int insertionIndex)
    {
        destinationFolderId = snapshot.RootFolderId;
        insertionIndex = int.MaxValue;
        if (target.Kind == HierarchyPlacementKind.IntoFolder)
        {
            if (target.TargetKind != OverviewNodeKind.Folder ||
                !snapshot.Folders.ContainsKey(target.TargetId))
            {
                diagnostics.Add(Error("hierarchy.destination_missing", "The destination folder no longer exists."));
                return false;
            }

            destinationFolderId = target.TargetId;
            return true;
        }

        if (folderIds.Count > 0)
        {
            if (target.TargetKind != OverviewNodeKind.Folder ||
                !snapshot.Folders.TryGetValue(target.TargetId, out var reference) ||
                reference.ParentId is not { } parentId)
            {
                diagnostics.Add(Error("hierarchy.invalid_insertion", "Folders can only be inserted beside another non-root folder."));
                return false;
            }

            if (folderIds.Contains(target.TargetId))
            {
                diagnostics.Add(Error("hierarchy.no_change", "The insertion target is part of the moving selection."));
                return false;
            }

            destinationFolderId = parentId;
            var remaining = OrderedFolders(snapshot, parentId)
                .Where(id => !folderIds.Contains(id))
                .ToList();
            var referencePosition = remaining.IndexOf(target.TargetId);
            insertionIndex = referencePosition + (target.Kind == HierarchyPlacementKind.AfterSibling ? 1 : 0);
            return referencePosition >= 0;
        }

        if (target.TargetKind != OverviewNodeKind.Sheet ||
            !snapshot.Sheets.TryGetValue(target.TargetId, out var sheetReference))
        {
            diagnostics.Add(Error("hierarchy.invalid_insertion", "Layouts can only be inserted beside another layout."));
            return false;
        }

        if (sheetIds.Contains(target.TargetId))
        {
            diagnostics.Add(Error("hierarchy.no_change", "The insertion target is part of the moving selection."));
            return false;
        }

        destinationFolderId = sheetReference.FolderId;
        var remainingSheets = OrderedSheets(snapshot, destinationFolderId)
            .Where(id => !sheetIds.Contains(id))
            .ToList();
        var sheetPosition = remainingSheets.IndexOf(target.TargetId);
        insertionIndex = sheetPosition + (target.Kind == HierarchyPlacementKind.AfterSibling ? 1 : 0);
        return sheetPosition >= 0;
    }

    private static Dictionary<Guid, List<Guid>> BuildFolderOrders(DocumentSnapshot snapshot) =>
        snapshot.Folders.Values
            .Where(folder => folder.Id != snapshot.RootFolderId && folder.ParentId is not null)
            .GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(folder => folder.Order)
                .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(folder => folder.Id)
                .Select(folder => folder.Id)
                .ToList());

    private static Dictionary<Guid, List<Guid>> BuildSheetOrders(DocumentSnapshot snapshot) =>
        snapshot.Sheets.Values
            .GroupBy(sheet => sheet.FolderId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(sheet => sheet.Order)
                .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(sheet => sheet.PageViewId)
                .Select(sheet => sheet.PageViewId)
                .ToList());

    private static IReadOnlyList<Guid> OrderedFolders(DocumentSnapshot snapshot, Guid parentId) =>
        BuildFolderOrders(snapshot).GetValueOrDefault(parentId) ?? [];

    private static IReadOnlyList<Guid> OrderedSheets(DocumentSnapshot snapshot, Guid folderId) =>
        BuildSheetOrders(snapshot).GetValueOrDefault(folderId) ?? [];

    private static void RemoveSelected(
        IDictionary<Guid, List<Guid>> groups,
        IReadOnlyCollection<Guid> selected)
    {
        foreach (var group in groups.Values) group.RemoveAll(selected.Contains);
    }

    private static List<Guid> RemoveCoveredFolders(
        IReadOnlyList<Guid> folderIds,
        DocumentSnapshot snapshot) =>
        folderIds.Where(folderId => snapshot.Folders.ContainsKey(folderId) &&
                                    !folderIds.Any(other => other != folderId &&
                                        IsDescendant(folderId, other, snapshot.Folders)))
            .ToList();

    private static IEnumerable<Guid> DescendantsAndSelf(
        Guid folderId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var result = new HashSet<Guid> { folderId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in folders.Values)
                if (folder.ParentId is { } parentId && result.Contains(parentId))
                    changed |= result.Add(folder.Id);
        }

        return result;
    }

    private static bool IsDescendant(
        Guid candidateId,
        Guid ancestorId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var visited = new HashSet<Guid>();
        var current = candidateId;
        while (visited.Add(current) && folders.TryGetValue(current, out var folder) &&
               folder.ParentId is { } parentId)
        {
            if (parentId == ancestorId) return true;
            current = parentId;
        }

        return false;
    }

    private static IEnumerable<Guid> StableDistinct(IEnumerable<Guid> ids)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
            if (seen.Add(id)) yield return id;
    }

    private static OperationPlan Failed(DocumentSnapshot snapshot, IReadOnlyList<Diagnostic> diagnostics) =>
        new(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, "Reorganize hierarchy", [], diagnostics);

    private static Diagnostic Error(string code, string message, Guid? subjectId = null) =>
        new(code, DiagnosticSeverity.Error, message, subjectId);
}
