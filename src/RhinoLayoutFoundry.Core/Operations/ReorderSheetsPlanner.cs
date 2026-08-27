using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record ReorderSheetsRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    Guid MovingSheetId,
    Guid? BeforeSheetId);

public sealed class ReorderSheetsPlanner : IOperationPlanner<ReorderSheetsRequest>
{
    public OperationPlan Plan(ReorderSheetsRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("reorder.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("reorder.stale_revision", "The Rhino document changed. Refresh and try again."));
        snapshot.Sheets.TryGetValue(request.MovingSheetId, out var moving);
        SheetSnapshot? before = null;
        if (request.BeforeSheetId is { } beforeId)
            snapshot.Sheets.TryGetValue(beforeId, out before);
        if (moving is null || (request.BeforeSheetId is not null && before is null))
            diagnostics.Add(Error("reorder.sheet_missing", "A reordered layout no longer exists."));
        else if (before is not null && moving.FolderId != before.FolderId)
            diagnostics.Add(Error("reorder.different_folder", "Move the layout into the destination folder before reordering it."));
        else if (before is not null && moving.PageViewId == before.PageViewId)
            diagnostics.Add(Error("reorder.no_change", "The layout is already at that position."));

        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error) || moving is null)
            return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                "Reorder layouts", [], diagnostics);

        var ordered = snapshot.Sheets.Values
            .Where(sheet => sheet.FolderId == moving.FolderId)
            .OrderBy(sheet => sheet.Order)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ordered.RemoveAll(sheet => sheet.PageViewId == moving.PageViewId);
        var insertAt = before is null
            ? ordered.Count
            : ordered.FindIndex(sheet => sheet.PageViewId == before.PageViewId);
        ordered.Insert(Math.Max(0, insertAt), moving);
        var expected = ordered.ToDictionary(sheet => sheet.PageViewId, sheet => sheet.Order);
        var next = ordered.Select((sheet, index) => (sheet.PageViewId, index))
            .ToDictionary(pair => pair.PageViewId, pair => pair.index);
        if (expected.All(pair => next[pair.Key] == pair.Value))
        {
            diagnostics.Add(Error("reorder.no_change", "The layouts are already in that order."));
            return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                "Reorder layouts", [], diagnostics);
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            "Reorder layouts",
            [new ReorderSheetsChange(moving.FolderId, expected, next)],
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
