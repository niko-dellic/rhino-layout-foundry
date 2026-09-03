using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record CreateNamedViewRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    NamedViewDefinition Definition);

public sealed class CreateNamedViewPlanner : IOperationPlanner<CreateNamedViewRequest>
{
    public OperationPlan Plan(CreateNamedViewRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = CommonDiagnostics(
            request.DocumentRuntimeSerialNumber,
            request.SourceRevision,
            snapshot);
        var definition = request.Definition;
        var name = definition.Name?.Trim() ?? string.Empty;
        var sessionId = definition.SessionId?.Trim() ?? string.Empty;

        if (name.Length == 0)
            diagnostics.Add(Error("named_view.name_required", "Enter a name for the named view."));
        else if (snapshot.NamedViews.Contains(name))
            diagnostics.Add(Error("named_view.duplicate_name", $"A named view called '{name}' already exists."));
        if (!definition.CameraLocation.IsFinite || !definition.CameraTarget.IsFinite ||
            !definition.CameraUp.IsFinite)
            diagnostics.Add(Error("named_view.coordinates_invalid", "Camera coordinates must be finite."));
        if (definition.CameraLocation.DistanceTo(definition.CameraTarget) <= 1e-9)
            diagnostics.Add(Error("named_view.camera_degenerate", "Camera location and target must differ."));
        if (definition.CameraUp.Length <= 1e-9)
            diagnostics.Add(Error("named_view.up_degenerate", "Camera up must be a non-zero vector."));
        else
        {
            var direction = Direction(definition.CameraLocation, definition.CameraTarget);
            var cosine = Math.Abs(direction.Dot(definition.CameraUp) /
                                  (direction.Length * definition.CameraUp.Length));
            if (cosine >= 0.9999)
                diagnostics.Add(Error("named_view.up_parallel", "Camera up cannot be parallel to its direction."));
        }
        if (!double.IsFinite(definition.LensLength) || definition.LensLength <= 0)
            diagnostics.Add(Error("named_view.lens_invalid", "Lens length must be greater than zero."));
        if (sessionId.Length == 0)
            diagnostics.Add(Error("automation.session_required", "An automation session ID is required."));

        var changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? Array.Empty<OperationChange>()
            : new OperationChange[]
            {
                new CreateNamedViewChange(definition with { Name = name, SessionId = sessionId }),
            };
        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Create named view {name}",
            changes,
            diagnostics);
    }

    private static Vector3Coordinates Direction(Point3Coordinates from, Point3Coordinates to) =>
        new(to.X - from.X, to.Y - from.Y, to.Z - from.Z);

    internal static List<Diagnostic> CommonDiagnostics(
        uint documentRuntimeSerialNumber,
        long sourceRevision,
        DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (documentRuntimeSerialNumber != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("automation.document_mismatch", "The active Rhino document changed."));
        if (sourceRevision != snapshot.Revision)
            diagnostics.Add(Error("automation.stale_revision", "The Rhino document changed. Refresh and try again."));
        return diagnostics;
    }

    internal static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record CreateClippingPlaneRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    ClippingPlaneDefinition Definition);

public sealed class CreateClippingPlanePlanner : IOperationPlanner<CreateClippingPlaneRequest>
{
    public OperationPlan Plan(CreateClippingPlaneRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = CreateNamedViewPlanner.CommonDiagnostics(
            request.DocumentRuntimeSerialNumber,
            request.SourceRevision,
            snapshot);
        var definition = request.Definition;
        var name = definition.Name?.Trim() ?? string.Empty;
        var sessionId = definition.SessionId?.Trim() ?? string.Empty;
        var viewportIds = definition.ViewportIds?.Where(id => id != Guid.Empty).Distinct().ToArray() ?? [];

        if (name.Length == 0)
            diagnostics.Add(Error("clipping_plane.name_required", "Enter a name for the clipping plane."));
        if (!definition.Origin.IsFinite || !definition.Normal.IsFinite || !definition.XAxis.IsFinite)
            diagnostics.Add(Error("clipping_plane.coordinates_invalid", "Clipping-plane coordinates must be finite."));
        if (definition.Normal.Length <= 1e-9 || definition.XAxis.Length <= 1e-9)
            diagnostics.Add(Error("clipping_plane.axes_degenerate", "Clipping-plane axes must be non-zero."));
        else
        {
            var cosine = Math.Abs(definition.Normal.Dot(definition.XAxis) /
                                  (definition.Normal.Length * definition.XAxis.Length));
            if (cosine >= 0.9999)
                diagnostics.Add(Error("clipping_plane.axes_parallel", "The clipping-plane X axis cannot be parallel to its normal."));
        }
        if (!double.IsFinite(definition.Width) || definition.Width <= 0 ||
            !double.IsFinite(definition.Height) || definition.Height <= 0)
            diagnostics.Add(Error("clipping_plane.size_invalid", "Clipping-plane width and height must be greater than zero."));
        if (viewportIds.Length == 0)
            diagnostics.Add(Error("clipping_plane.viewport_required", "Choose at least one viewport to clip."));
        if (sessionId.Length == 0)
            diagnostics.Add(Error("automation.session_required", "An automation session ID is required."));

        var changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? Array.Empty<OperationChange>()
            : new OperationChange[]
            {
                new CreateClippingPlaneChange(definition with
                {
                    Name = name,
                    SessionId = sessionId,
                    ViewportIds = viewportIds,
                }),
            };
        return new OperationPlan(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            $"Create clipping plane {name}",
            changes,
            diagnostics);
    }

    private static Diagnostic Error(string code, string message) =>
        CreateNamedViewPlanner.Error(code, message);
}
