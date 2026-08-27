using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record CreateSheetRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    string Name);

public sealed class CreateSheetPlanner : IOperationPlanner<CreateSheetRequest>
{
    public OperationPlan Plan(CreateSheetRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<Diagnostic>();
        var name = request.Name?.Trim() ?? string.Empty;
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error("sheet.document_mismatch", "The active Rhino document changed."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error("sheet.stale_revision", "The Rhino document changed. Refresh and try again."));
        }

        if (!snapshot.Folders.ContainsKey(request.DestinationFolderId))
        {
            diagnostics.Add(Error("sheet.destination_missing", "The destination folder no longer exists."));
        }

        if (name.Length == 0)
        {
            diagnostics.Add(Error("sheet.name_required", "Enter a name for the new layout."));
        }
        else if (snapshot.Sheets.Values.Any(sheet =>
                     string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(Error("sheet.duplicate_name", $"A layout named '{name}' already exists."));
        }

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var nextOrder = snapshot.Sheets.Values
                .Where(sheet => sheet.FolderId == request.DestinationFolderId)
                .Select(sheet => sheet.Order)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            changes.Add(new CreateSheetChange(request.DestinationFolderId, name, nextOrder));
            diagnostics.Add(new Diagnostic(
                "sheet.undo_unavailable",
                DiagnosticSeverity.Warning,
                "Rhino does not support Undo for layout creation. The new layout can be deleted from Rhino's Layouts panel."));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            "Create layout",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
