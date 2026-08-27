using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record UpdateDetailDisplayModesRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<Guid> DetailViewportIds,
    Guid DisplayModeId);

public sealed class UpdateDetailDisplayModesPlanner : IOperationPlanner<UpdateDetailDisplayModesRequest>
{
    public OperationPlan Plan(UpdateDetailDisplayModesRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = ContextDiagnostics(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        var ids = request.DetailViewportIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            diagnostics.Add(Error("inline.detail_empty", "Choose at least one detail viewport."));
        if (!snapshot.DisplayModeIds.Contains(request.DisplayModeId))
            diagnostics.Add(Error("inline.display_mode_missing", "The selected Rhino display mode is unavailable."));
        var existing = snapshot.Sheets.Values.SelectMany(sheet => sheet.DetailIds).ToHashSet();
        foreach (var id in ids.Where(id => !existing.Contains(id)))
            diagnostics.Add(new Diagnostic("inline.detail_missing", DiagnosticSeverity.Error,
                "A targeted detail viewport no longer exists.", id));

        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new UpdateDetailDisplayModesChange(ids, request.DisplayModeId)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Set display mode on {ids.Length} detail{(ids.Length == 1 ? string.Empty : "s")}",
            changes,
            diagnostics);
    }

    private static List<Diagnostic> ContextDiagnostics(uint serial, long revision, DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (serial != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("inline.document_mismatch", "The active Rhino document changed."));
        if (revision != snapshot.Revision)
            diagnostics.Add(Error("inline.stale_revision", "The Rhino document changed before the edit was applied."));
        return diagnostics;
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record SetPrintInclusionRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<Guid> SheetPageViewIds,
    bool IncludeInPrintAll);

public sealed class SetPrintInclusionPlanner : IOperationPlanner<SetPrintInclusionRequest>
{
    public OperationPlan Plan(SetPrintInclusionRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("print.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("print.stale_revision", "The Rhino document changed before the edit was applied."));

        var ids = request.SheetPageViewIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
            diagnostics.Add(Error("print.empty", "This row does not contain any layouts."));
        foreach (var id in ids.Where(id => !snapshot.Sheets.ContainsKey(id)))
            diagnostics.Add(new Diagnostic("print.sheet_missing", DiagnosticSeverity.Error,
                "A targeted layout no longer exists.", id));

        var expected = ids.Where(snapshot.Sheets.ContainsKey)
            .ToDictionary(id => id, id => snapshot.Sheets[id].IncludeInPrintAll);
        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new SetPrintInclusionChange(expected, request.IncludeInPrintAll)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            request.IncludeInPrintAll ? "Include layouts in Print All" : "Exclude layouts from Print All",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
