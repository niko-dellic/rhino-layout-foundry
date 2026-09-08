using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Diagnostics;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record FlipClippingPlaneChange(Guid ObjectId) : OperationChange;
public sealed record FlipClippingPlaneRequest(uint DocumentRuntimeSerialNumber, long SourceRevision, Guid ObjectId);

public sealed class FlipClippingPlanePlanner : IOperationPlanner<FlipClippingPlaneRequest>
{
    public OperationPlan Plan(FlipClippingPlaneRequest request, DocumentSnapshot snapshot)
    {
        var diagnostics = CreateNamedViewPlanner.CommonDiagnostics(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        var clip = snapshot.ClippingPlanes.FirstOrDefault(c => c.ObjectId == request.ObjectId);
        if (clip is null || string.IsNullOrWhiteSpace(clip.SessionId))
            diagnostics.Add(CreateNamedViewPlanner.Error("clipping_plane.not_owned", "Only an existing automation-created clipping plane can be flipped."));
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Flip clipping direction: {clip?.Name ?? request.ObjectId.ToString()}",
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? [] : [new FlipClippingPlaneChange(request.ObjectId)], diagnostics);
    }
}
