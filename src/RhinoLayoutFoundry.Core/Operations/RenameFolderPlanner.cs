using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record RenameFolderRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid FolderId,
    string ExpectedName,
    string NewName);

public sealed class RenameFolderPlanner : IOperationPlanner<RenameFolderRequest>
{
    public OperationPlan Plan(RenameFolderRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = ValidateContext(request, snapshot);
        snapshot.Folders.TryGetValue(request.FolderId, out var folder);
        if (request.FolderId == snapshot.RootFolderId)
        {
            diagnostics.Add(Error("folder.root_immutable", "The hierarchy root cannot be renamed."));
        }
        else if (folder is null)
        {
            diagnostics.Add(Error("folder.missing", "The folder no longer exists."));
        }
        else if (!string.Equals(folder.Name, request.ExpectedName, StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "folder.before_value_changed",
                $"The folder is now named '{folder.Name}', so the rename was not applied."));
        }

        var name = request.NewName.Trim();
        if (name.Length == 0)
        {
            diagnostics.Add(Error("folder.empty_name", "A folder name cannot be empty."));
        }

        if (folder is not null && snapshot.Folders.Values.Any(candidate =>
                candidate.Id != folder.Id &&
                candidate.ParentId == folder.ParentId &&
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(Error(
                "folder.duplicate_name",
                $"A folder named '{name}' already exists in this location."));
        }

        var changes = new List<OperationChange>();
        if (folder is not null && diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            changes.Add(new RenameFolderChange(
                folder.Id,
                folder.ParentId!.Value,
                folder.Name,
                name));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Rename folder {request.ExpectedName}",
            changes,
            diagnostics);
    }

    private static List<Diagnostic> ValidateContext(
        RenameFolderRequest request,
        DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error("folder.document_mismatch", "The active Rhino document changed."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error("folder.stale_revision", "The Rhino document changed. Refresh and try again."));
        }

        return diagnostics;
    }

    private static Diagnostic Error(string code, string message)
    {
        return new Diagnostic(code, DiagnosticSeverity.Error, message);
    }
}
