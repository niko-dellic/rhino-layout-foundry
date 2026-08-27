using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record SetObserverCanvasStateRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    ObserverCanvasState NewState,
    string UndoDescription = "Organize observer canvas");

public sealed class SetObserverCanvasStatePlanner : IOperationPlanner<SetObserverCanvasStateRequest>
{
    public OperationPlan Plan(SetObserverCanvasStateRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request.NewState);
        var diagnostics = new List<Diagnostic>();
        if (request.DocumentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
        {
            diagnostics.Add(Error("observer.document_mismatch", "The active Rhino document changed."));
        }

        if (request.SourceRevision != snapshot.Revision)
        {
            diagnostics.Add(Error("observer.stale_revision", "The Rhino document changed. Refresh and try again."));
        }

        if (request.NewState.LayoutAlgorithmVersion <= 0)
        {
            diagnostics.Add(Error("observer.invalid_layout_version", "The observer layout algorithm version is invalid."));
        }

        ValidatePoints(request.NewState.FolderOrigins, "folder", diagnostics);
        ValidatePoints(request.NewState.SheetPlacements, "sheet", diagnostics);
        var hasErrors = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error);
        var changed = !ObserverCanvasStateComparer.ContentEquals(snapshot.Canvas, request.NewState);
        if (!hasErrors && !changed)
        {
            diagnostics.Add(Error("observer.no_change", "The observer board is already arranged this way."));
        }

        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            string.IsNullOrWhiteSpace(request.UndoDescription)
                ? "Organize observer canvas"
                : request.UndoDescription.Trim(),
            hasErrors || !changed
                ? []
                : [new SetObserverCanvasStateChange(snapshot.Canvas, request.NewState)],
            diagnostics);
    }

    private static void ValidatePoints(
        IReadOnlyDictionary<Guid, ObserverPointRecord> points,
        string kind,
        ICollection<Diagnostic> diagnostics)
    {
        foreach (var pair in points)
        {
            if (pair.Key == Guid.Empty || !double.IsFinite(pair.Value.X) || !double.IsFinite(pair.Value.Y))
            {
                diagnostics.Add(new Diagnostic(
                    "observer.invalid_placement",
                    DiagnosticSeverity.Error,
                    $"An observer {kind} placement is invalid.",
                    pair.Key == Guid.Empty ? null : pair.Key));
            }
        }
    }

    private static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}
