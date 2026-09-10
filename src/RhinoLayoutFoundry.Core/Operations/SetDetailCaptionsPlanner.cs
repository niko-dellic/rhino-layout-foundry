using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Diagnostics;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record SetDetailCaptionsRequest(uint DocumentRuntimeSerialNumber, long SourceRevision,
    HierarchyScope Scope, DetailCaptionSettings Settings);
public sealed record SetDetailCaptionsChange(HierarchyScope Scope, DetailCaptionSettings Settings) : OperationChange;

public sealed class SetDetailCaptionsPlanner : IOperationPlanner<SetDetailCaptionsRequest>
{
    public OperationPlan Plan(SetDetailCaptionsRequest request, DocumentSnapshot snapshot)
    {
        var diagnostics = CreateNamedViewPlanner.CommonDiagnostics(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        var exists = request.Scope.Kind switch
        {
            HierarchyScopeKind.Folder => snapshot.Folders.ContainsKey(request.Scope.Id),
            HierarchyScopeKind.Sheet => snapshot.Sheets.ContainsKey(request.Scope.Id),
            HierarchyScopeKind.Detail => snapshot.Sheets.Values.Any(s => s.Details.Any(d => d.DetailViewportId == request.Scope.Id)),
            _ => false
        };
        if (!exists) diagnostics.Add(CreateNamedViewPlanner.Error("caption.target_missing", "The caption target no longer exists."));
        if (!Enum.IsDefined(request.Settings.Name) || !Enum.IsDefined(request.Settings.Scale))
            diagnostics.Add(CreateNamedViewPlanner.Error("caption.invalid_setting", "Choose Inherit, Show, or Hide."));
        return new(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, "Set detail captions",
            diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ? [] : [new SetDetailCaptionsChange(request.Scope, request.Settings)], diagnostics);
    }
}
