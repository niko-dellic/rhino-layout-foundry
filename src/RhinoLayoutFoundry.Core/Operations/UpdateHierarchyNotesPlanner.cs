using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record UpdateHierarchyNotesRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    IReadOnlyList<OverviewNodeKey> Targets,
    string Notes);

public sealed class UpdateHierarchyNotesPlanner : IOperationPlanner<UpdateHierarchyNotesRequest>
{
    public OperationPlan Plan(UpdateHierarchyNotesRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("notes.document_mismatch", "The active Rhino document changed."));
        if (request.SourceRevision != snapshot.Revision)
            diagnostics.Add(Error("notes.stale_revision", "The Rhino document changed before the notes were saved."));

        var targets = request.Targets
            .Where(item => item.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet)
            .Distinct()
            .ToArray();
        if (targets.Length == 0)
            diagnostics.Add(Error("notes.empty_selection", "Select at least one folder or layout."));

        var missing = targets.Where(target => target.Kind switch
        {
            OverviewNodeKind.Folder => !snapshot.Folders.ContainsKey(target.Id),
            OverviewNodeKind.Sheet => !snapshot.Sheets.ContainsKey(target.Id),
            _ => true,
        }).ToArray();
        if (missing.Length > 0)
            diagnostics.Add(Error("notes.target_missing", "A selected folder or layout no longer exists."));

        var next = request.Notes ?? string.Empty;
        var expectedFolders = targets
            .Where(target => target.Kind == OverviewNodeKind.Folder && snapshot.Folders.ContainsKey(target.Id))
            .ToDictionary(target => target.Id, target => snapshot.Folders[target.Id].Notes ?? string.Empty);
        var expectedSheets = targets
            .Where(target => target.Kind == OverviewNodeKind.Sheet && snapshot.Sheets.ContainsKey(target.Id))
            .ToDictionary(target => target.Id, target => snapshot.Sheets[target.Id].Notes ?? string.Empty);
        var newFolders = expectedFolders.Keys.ToDictionary(id => id, _ => next);
        var newSheets = expectedSheets.Keys.ToDictionary(id => id, _ => next);
        var changed = expectedFolders.Any(pair => !string.Equals(pair.Value, next, StringComparison.Ordinal)) ||
                      expectedSheets.Any(pair => !string.Equals(pair.Value, next, StringComparison.Ordinal));

        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error) ||
                                                 !changed
            ? []
            : [new UpdateHierarchyNotesChange(expectedFolders, newFolders, expectedSheets, newSheets)];
        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            targets.Length == 1 ? "Update notes" : $"Update notes on {targets.Length} items",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
