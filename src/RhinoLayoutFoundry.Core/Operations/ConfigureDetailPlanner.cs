using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Diagnostics;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record ConfigureDetailChange(Guid DetailViewportId, double ScaleDenominator,
    Point3Coordinates Target, string Name) : OperationChange;

public sealed record ConfigureDetailRequest(uint DocumentRuntimeSerialNumber, long SourceRevision,
    Guid DetailViewportId, double ScaleDenominator, Point3Coordinates Target, string Name);

public sealed class ConfigureDetailPlanner : IOperationPlanner<ConfigureDetailRequest>
{
    public OperationPlan Plan(ConfigureDetailRequest request, DocumentSnapshot snapshot)
    {
        var diagnostics = CreateNamedViewPlanner.CommonDiagnostics(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        if (!snapshot.Sheets.Values.SelectMany(s => s.Details).Any(d => d.DetailViewportId == request.DetailViewportId))
            diagnostics.Add(CreateNamedViewPlanner.Error("detail.missing", "The target detail no longer exists."));
        if (!double.IsFinite(request.ScaleDenominator) || request.ScaleDenominator < 1 || request.ScaleDenominator > 10000 || !request.Target.IsFinite)
            diagnostics.Add(CreateNamedViewPlanner.Error("detail.frame_invalid", "Provide a finite target and a scale between 1 and 10000."));
        if (string.IsNullOrWhiteSpace(request.Name))
            diagnostics.Add(CreateNamedViewPlanner.Error("detail.name_required", "Provide a readable detail name."));
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            $"Frame detail {request.Name} at 1:{request.ScaleDenominator:g}",
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? [] :
                [new ConfigureDetailChange(request.DetailViewportId, request.ScaleDenominator, request.Target, request.Name.Trim())], diagnostics);
    }
}
