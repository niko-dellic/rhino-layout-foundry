using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;

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

        var changes = new List<OperationChange>();
        if (diagnostics.All(item => item.Severity != DiagnosticSeverity.Error))
        {
            changes.Add(new AssignNamedViewToDetailsChange(detailIds, name));
            var targetSet = detailIds.ToHashSet();
            var affectedSheets = snapshot.Sheets.Values
                .Where(sheet => sheet.DetailIds.Any(targetSet.Contains))
                .ToArray();
            var bindingOverrides = new Dictionary<Guid, SheetNamingBinding?>();
            foreach (var sheet in affectedSheets.Where(sheet =>
                         sheet.NamingBinding is not null && string.Equals(
                             sheet.Name,
                             sheet.NamingBinding.LastGeneratedName,
                             StringComparison.Ordinal)))
            {
                var assigned = sheet.NamingBinding!.NamedViewAssignments.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var detailId in sheet.DetailIds.Where(targetSet.Contains)) assigned[detailId] = name;
                bindingOverrides[sheet.PageViewId] = sheet.NamingBinding with
                {
                    NamedViewAssignments = assigned,
                };
            }
            var linked = LinkedSheetNaming.Preview(
                snapshot,
                bindingOverrides: bindingOverrides,
                affectedSheetIds: affectedSheets.Select(sheet => sheet.PageViewId).ToHashSet());
            diagnostics.AddRange(linked.Diagnostics);
            if (linked.Change is not null) changes.Add(linked.Change);
        }
        if (diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)) changes.Clear();

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Assign named view {name}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
