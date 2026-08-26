using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record RenameSheetRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid PageViewId,
    string ExpectedName,
    string NewName);

public sealed class RenameSheetPlanner : IOperationPlanner<RenameSheetRequest>
{
    public OperationPlan Plan(RenameSheetRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);

        var diagnostics = new List<Diagnostic>();
        var changes = new List<OperationChange>();

        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error(
                "rename.document_mismatch",
                "The active Rhino document changed before the rename could be planned."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error(
                "rename.stale_revision",
                "The Rhino document changed after this edit was started. Refresh and try again."));
        }

        if (!snapshot.Sheets.TryGetValue(request.PageViewId, out var sheet))
        {
            diagnostics.Add(Error(
                "rename.sheet_missing",
                "The layout sheet no longer exists.",
                request.PageViewId));
        }
        else if (!string.Equals(sheet.Name, request.ExpectedName, StringComparison.Ordinal))
        {
            diagnostics.Add(Error(
                "rename.before_value_changed",
                $"The layout was renamed from '{request.ExpectedName}' to '{sheet.Name}' outside Foundry.",
                request.PageViewId));
        }

        var newName = request.NewName.Trim();
        if (newName.Length == 0)
        {
            diagnostics.Add(Error(
                "rename.empty_name",
                "A layout name cannot be empty.",
                request.PageViewId));
        }

        if (sheet is not null && string.Equals(sheet.Name, newName, StringComparison.Ordinal))
        {
            diagnostics.Add(new Diagnostic(
                "rename.no_change",
                DiagnosticSeverity.Information,
                "The layout already has that name.",
                request.PageViewId));
        }

        var duplicate = snapshot.Sheets.Values.FirstOrDefault(candidate =>
            candidate.PageViewId != request.PageViewId &&
            string.Equals(candidate.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            diagnostics.Add(Error(
                "rename.duplicate_name",
                $"Another layout is already named '{duplicate.Name}'.",
                duplicate.PageViewId));
        }

        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error) &&
            sheet is not null &&
            !string.Equals(sheet.Name, newName, StringComparison.Ordinal))
        {
            changes.Add(new RenameSheetChange(request.PageViewId, sheet.Name, newName));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Rename layout to {newName}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message, Guid? entityId = null)
    {
        return new Diagnostic(code, DiagnosticSeverity.Error, message, entityId);
    }
}
