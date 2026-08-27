using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record MoveHierarchySelectionRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    IReadOnlyList<Guid> FolderIds,
    IReadOnlyList<Guid> SheetIds);

/// <summary>
/// Composes the existing folder and sheet planners into one atomic hierarchy
/// operation while removing descendants already covered by a selected folder.
/// </summary>
public sealed class MoveHierarchySelectionPlanner : IOperationPlanner<MoveHierarchySelectionRequest>
{
    public OperationPlan Plan(MoveHierarchySelectionRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var selectedFolders = request.FolderIds
            .Where(id => id != snapshot.RootFolderId)
            .Distinct()
            .ToHashSet();
        selectedFolders.RemoveWhere(folderId => selectedFolders.Any(other =>
            other != folderId && IsDescendant(snapshot, folderId, other)));
        var coveredFolders = selectedFolders
            .SelectMany(folderId => Descendants(snapshot, folderId))
            .ToHashSet();
        var selectedSheets = request.SheetIds
            .Distinct()
            .Where(sheetId => snapshot.Sheets.TryGetValue(sheetId, out var sheet) &&
                              !coveredFolders.Contains(sheet.FolderId))
            .ToArray();
        if (selectedFolders.Count == 0 && selectedSheets.Length == 0)
        {
            return new OperationPlan(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                "Move hierarchy selection",
                [],
                [new Diagnostic("move_selection.empty", DiagnosticSeverity.Error,
                    "The selection does not contain a movable folder or layout.")]);
        }

        var plans = new List<OperationPlan>();
        if (selectedFolders.Count > 0)
            plans.Add(new MoveFoldersPlanner().Plan(
                new MoveFoldersRequest(request.DocumentRuntimeSerialNumber, request.SourceRevision,
                    request.DestinationFolderId, selectedFolders.ToArray()), snapshot));
        if (selectedSheets.Length > 0)
            plans.Add(new MoveSheetsPlanner().Plan(
                new MoveSheetsRequest(request.DocumentRuntimeSerialNumber, request.SourceRevision,
                    request.DestinationFolderId, selectedSheets), snapshot));

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            "Move hierarchy selection",
            plans.SelectMany(plan => plan.Changes).ToArray(),
            plans.SelectMany(plan => plan.Diagnostics).ToArray());
    }

    private static bool IsDescendant(DocumentSnapshot snapshot, Guid folderId, Guid ancestorId)
    {
        var visited = new HashSet<Guid>();
        Guid? current = folderId;
        while (current is { } id && visited.Add(id) && snapshot.Folders.TryGetValue(id, out var folder))
        {
            if (id == ancestorId) return true;
            current = folder.ParentId;
        }

        return false;
    }

    private static IReadOnlySet<Guid> Descendants(DocumentSnapshot snapshot, Guid folderId)
    {
        var result = new HashSet<Guid> { folderId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var folder in snapshot.Folders.Values)
                if (folder.ParentId is { } parent && result.Contains(parent))
                    changed |= result.Add(folder.Id);
        }

        return result;
    }
}
