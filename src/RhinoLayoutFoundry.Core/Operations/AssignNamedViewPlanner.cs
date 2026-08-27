using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record AssignNamedViewRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<Guid> DetailViewportIds,
    string NamedViewName);

public sealed class AssignNamedViewPlanner : IOperationPlanner<AssignNamedViewRequest>
{
    public OperationPlan Plan(AssignNamedViewRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("named_view.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("named_view.stale_revision", "The Rhino document changed. Refresh and try again."));

        var name = request.NamedViewName?.Trim() ?? string.Empty;
        if (name.Length == 0 || !snapshot.NamedViews.Contains(name))
            diagnostics.Add(Error("named_view.missing", "The selected Rhino named view no longer exists."));
        var allDetailIds = snapshot.Sheets.Values.SelectMany(sheet => sheet.DetailIds).ToHashSet();
        var detailIds = request.DetailViewportIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (detailIds.Length == 0)
            diagnostics.Add(Error("named_view.no_details", "Choose one or more detail viewports."));
        foreach (var detailId in detailIds.Where(id => !allDetailIds.Contains(id)))
        {
            diagnostics.Add(new Diagnostic(
                "named_view.detail_missing",
                DiagnosticSeverity.Error,
                "A targeted detail viewport no longer exists.",
                detailId));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Assign named view {name}",
            diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
                ? []
                : [new AssignNamedViewToDetailsChange(detailIds, name)],
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
