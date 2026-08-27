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

        var changes = new List<OperationChange>();
        if (folder is not null && diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var descendants = Descendants(request.FolderId, snapshot.Folders);
            var sheets = snapshot.Sheets.Values
                .Where(sheet => descendants.Contains(sheet.FolderId))
                .OrderBy(sheet => sheet.Order)
                .Select(sheet => sheet.PageViewId)
                .ToArray();
            changes.Add(new DeleteFolderChange(
                folder.Id,
                folder.ParentId!.Value,
                folder.Name,
                descendants.Where(id => id != folder.Id).ToArray(),
                sheets));
            if (sheets.Length > 0)
                diagnostics.Add(new Diagnostic(
                    "folder.delete_contains_sheets",
                    DiagnosticSeverity.Warning,
                    $"Deleting this folder will permanently delete {sheets.Length} Rhino layout{(sheets.Length == 1 ? string.Empty : "s")} inside it."));
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
}
