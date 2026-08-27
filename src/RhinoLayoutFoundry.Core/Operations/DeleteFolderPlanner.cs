using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record DeleteFolderRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid FolderId,
    string ExpectedName);

public sealed class DeleteFolderPlanner : IOperationPlanner<DeleteFolderRequest>
{
    public OperationPlan Plan(DeleteFolderRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error("folder.document_mismatch", "The active Rhino document changed."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error("folder.stale_revision", "The Rhino document changed. Refresh and try again."));
        }

        snapshot.Folders.TryGetValue(request.FolderId, out var folder);
        if (request.FolderId == snapshot.RootFolderId)
        {
            diagnostics.Add(Error("folder.root_immutable", "The hierarchy root cannot be deleted."));
        }
        else if (folder is null)
        {
            diagnostics.Add(Error("folder.missing", "The folder no longer exists."));
        }
        else if (!string.Equals(folder.Name, request.ExpectedName, StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "folder.before_value_changed",
                $"The folder is now named '{folder.Name}', so it was not deleted."));
        }

        if (snapshot.Folders.Values.Any(candidate => candidate.ParentId == request.FolderId))
        {
            diagnostics.Add(Error(
                "folder.has_children",
                "Move or delete this folder's nested folders before deleting it."));
        }

        if (snapshot.Sheets.Values.Any(sheet => sheet.FolderId == request.FolderId))
        {
            diagnostics.Add(Error(
                "folder.has_sheets",
                "Move this folder's sheets before deleting it."));
        }

        var changes = new List<OperationChange>();
        if (folder is not null && diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            changes.Add(new DeleteFolderChange(
                folder.Id,
                folder.ParentId!.Value,
                folder.Name));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Delete folder {request.ExpectedName}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message)
    {
        return new Diagnostic(code, DiagnosticSeverity.Error, message);
    }
}
