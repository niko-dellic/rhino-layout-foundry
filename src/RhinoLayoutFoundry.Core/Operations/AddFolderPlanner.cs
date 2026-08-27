using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record AddFolderRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid FolderId,
    Guid ParentFolderId,
    string Name);

public sealed class AddFolderPlanner : IOperationPlanner<AddFolderRequest>
{
    public OperationPlan Plan(AddFolderRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error(
                "folder.document_mismatch",
                "The active Rhino document changed before the folder could be created."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error(
                "folder.stale_revision",
                "The Rhino document changed after folder creation started. Review the hierarchy and try again."));
        }

        if (request.FolderId == Guid.Empty || snapshot.Folders.ContainsKey(request.FolderId))
        {
            diagnostics.Add(Error(
                "folder.invalid_id",
                "The new folder identity is invalid or already exists.",
                request.FolderId));
        }

        if (!snapshot.Folders.ContainsKey(request.ParentFolderId))
        {
            diagnostics.Add(Error(
                "folder.parent_missing",
                "The destination folder no longer exists.",
                request.ParentFolderId));
        }

        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            diagnostics.Add(Error(
                "folder.empty_name",
                "A folder name cannot be empty.",
                request.FolderId));
        }

        var duplicate = snapshot.Folders.Values.FirstOrDefault(folder =>
            folder.ParentId == request.ParentFolderId &&
            string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            diagnostics.Add(Error(
                "folder.duplicate_name",
                $"A folder named '{duplicate.Name}' already exists in this location.",
                duplicate.Id));
        }

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var order = snapshot.Folders.Values
                .Where(folder => folder.ParentId == request.ParentFolderId)
                .Select(folder => folder.Order)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            changes.Add(new AddFolderChange(
                request.FolderId,
                request.ParentFolderId,
                name,
                order));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Create folder {name}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message, Guid? entityId = null)
    {
        return new Diagnostic(code, DiagnosticSeverity.Error, message, entityId);
    }
}
