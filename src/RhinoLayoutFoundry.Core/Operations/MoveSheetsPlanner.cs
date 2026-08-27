using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record MoveSheetsRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid DestinationFolderId,
    IReadOnlyList<Guid> SheetPageViewIds);

public sealed class MoveSheetsPlanner : IOperationPlanner<MoveSheetsRequest>
{
    public OperationPlan Plan(MoveSheetsRequest request, DocumentSnapshot snapshot)
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

        var sheetIds = request.SheetPageViewIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (sheetIds.Length == 0)
        {
            diagnostics.Add(Error("move.empty_selection", "Select one or more sheets or details to move."));
        }

        foreach (var sheetId in sheetIds.Where(id => !snapshot.Sheets.ContainsKey(id)))
        {
            diagnostics.Add(new Diagnostic(
                "move.sheet_missing",
                DiagnosticSeverity.Error,
                "A selected layout sheet no longer exists.",
                sheetId));
        }

        var movable = sheetIds
            .Where(snapshot.Sheets.ContainsKey)
            .Select(id => snapshot.Sheets[id])
            .Where(sheet => sheet.FolderId != request.DestinationFolderId)
            .ToArray();
        if (sheetIds.Length > 0 && movable.Length == 0 &&
            diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            diagnostics.Add(Error("move.already_in_folder", "The selected sheets are already in this folder."));
        }

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            var nextOrder = snapshot.Sheets.Values
                .Where(sheet => sheet.FolderId == request.DestinationFolderId)
                .Select(sheet => sheet.Order)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            changes.AddRange(movable.Select((sheet, index) => new MoveSheetChange(
                sheet.PageViewId,
                sheet.FolderId,
                request.DestinationFolderId,
                nextOrder + index)));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Move {movable.Length} layout{(movable.Length == 1 ? string.Empty : "s")}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message)
    {
        return new Diagnostic(code, DiagnosticSeverity.Error, message);
    }
}
