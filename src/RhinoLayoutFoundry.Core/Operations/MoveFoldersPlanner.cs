using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record MoveFoldersRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    IReadOnlyList<Guid> FolderIds);

public sealed class MoveFoldersPlanner : IOperationPlanner<MoveFoldersRequest>
{
    public OperationPlan Plan(MoveFoldersRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error("move.document_mismatch", "The active Rhino document changed."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error("move.stale_revision", "The Rhino document changed. Refresh and try again."));
        }

        if (!snapshot.Folders.ContainsKey(request.DestinationFolderId))
        {
            diagnostics.Add(Error("move.destination_missing", "The destination folder no longer exists."));
        }

        var folderIds = request.FolderIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (folderIds.Length == 0)
        {
            diagnostics.Add(Error("move.empty_selection", "Select a folder to move."));
        }

        foreach (var folderId in folderIds)
        {
            if (folderId == snapshot.RootFolderId)
            {
                diagnostics.Add(Error("move.root_folder", "The hierarchy root cannot be moved."));
            }
            else if (!snapshot.Folders.ContainsKey(folderId))
            {
                diagnostics.Add(new Diagnostic(
                    "move.folder_missing",
                    DiagnosticSeverity.Error,
                    "A selected folder no longer exists.",
                    folderId));
            }
            else if (folderId == request.DestinationFolderId ||
                     IsDescendant(request.DestinationFolderId, folderId, snapshot.Folders))
            {
                diagnostics.Add(new Diagnostic(
                    "move.folder_cycle",
                    DiagnosticSeverity.Error,
                    "A folder cannot be moved inside itself or one of its descendants.",
                    folderId));
            }
        }

        var movable = folderIds
            .Where(id => snapshot.Folders.ContainsKey(id) && id != snapshot.RootFolderId)
            .Select(id => snapshot.Folders[id])
            .Where(folder => folder.ParentId != request.DestinationFolderId)
            .ToArray();
        if (folderIds.Length > 0 && movable.Length == 0 && diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(Error("move.already_in_folder", "The selected folders are already in this location."));
        }

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var nextOrder = snapshot.Folders.Values
                .Where(folder => folder.ParentId == request.DestinationFolderId)
                .Select(folder => folder.Order)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            changes.AddRange(movable.Select((folder, index) => new MoveFolderChange(
                folder.Id,
                folder.ParentId ?? snapshot.RootFolderId,
                request.DestinationFolderId,
                nextOrder + index)));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Move {movable.Length} folder{(movable.Length == 1 ? string.Empty : "s")}",
            changes,
            diagnostics);
    }

    private static bool IsDescendant(
        Guid candidateId,
        Guid ancestorId,
        IReadOnlyDictionary<Guid, FolderRecord> folders)
    {
        var visited = new HashSet<Guid>();
        var currentId = candidateId;
        while (folders.TryGetValue(currentId, out var folder) && visited.Add(currentId))
        {
            if (folder.ParentId == ancestorId)
            {
                return true;
            }

            if (folder.ParentId is not { } parentId)
            {
                break;
            }

            currentId = parentId;
        }

        return false;
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
