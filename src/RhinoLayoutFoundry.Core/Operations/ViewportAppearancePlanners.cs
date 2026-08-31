using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.Core.Operations;

public sealed record SetHierarchyViewportRulesRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    HierarchyScope Scope,
    IReadOnlyList<LayerVisibilityRule>? LayerRules = null,
    IReadOnlyList<ObjectDisplayRule>? ObjectDisplayRules = null);

public sealed class SetHierarchyViewportRulesPlanner : IOperationPlanner<SetHierarchyViewportRulesRequest>
{
    public OperationPlan Plan(SetHierarchyViewportRulesRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = Context(request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        if (!ScopeExists(request.Scope, snapshot))
            diagnostics.Add(Error("appearance.scope_missing", "The selected hierarchy item no longer exists."));

        var current = snapshot.AppearanceRules.LastOrDefault(item => item.Scope == request.Scope);
        var layers = (request.LayerRules ?? current?.LayerRules ?? [])
            .GroupBy(rule => rule.Layer.LayerId)
            .Select(group => group.Last())
            .ToArray();
        var objects = (request.ObjectDisplayRules ?? current?.ObjectDisplayRules ?? [])
            .GroupBy(rule => rule.Selector)
            .Select(group => group.Last())
            .ToArray();

        foreach (var rule in layers)
        {
            if (rule.Layer.LayerId == Guid.Empty || !snapshot.LayerSnapshots.ContainsKey(rule.Layer.LayerId))
                diagnostics.Add(Error("appearance.layer_missing", $"Layer '{rule.Layer.FullPath}' is unavailable."));
        }
        foreach (var rule in objects)
        {
            if (!snapshot.DisplayModeIds.Contains(rule.DisplayModeId))
                diagnostics.Add(Error("appearance.display_mode_missing",
                    $"Display mode '{rule.DisplayModeName}' is unavailable."));
            if (rule.Selector.Kind == ObjectDisplaySelectorKind.ExactObject &&
                (rule.Selector.ObjectId is not { } objectId || !snapshot.ModelObjects.ContainsKey(objectId)))
                diagnostics.Add(Error("appearance.object_missing", "A selected model object is unavailable."));
            if (rule.Selector.Kind == ObjectDisplaySelectorKind.Layer &&
                (rule.Selector.LayerId is not { } layerId || !snapshot.LayerSnapshots.ContainsKey(layerId)))
                diagnostics.Add(Error("appearance.selector_layer_missing", "A selected object layer is unavailable."));
        }

        var next = layers.Length == 0 && objects.Length == 0
            ? null
            : new HierarchyViewportRuleSet(request.Scope, layers, objects);
        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new SetHierarchyViewportRulesChange(request.Scope, current, next)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            "Update viewport appearance rules", changes, diagnostics);
    }

    internal static bool ScopeExists(HierarchyScope scope, DocumentSnapshot snapshot) => scope.Kind switch
    {
        HierarchyScopeKind.Folder => snapshot.Folders.ContainsKey(scope.Id),
        HierarchyScopeKind.Sheet => snapshot.Sheets.ContainsKey(scope.Id),
        HierarchyScopeKind.Detail => snapshot.Sheets.Values.Any(sheet => sheet.DetailIds.Contains(scope.Id)),
        _ => false,
    };

    internal static List<Diagnostic> Context(uint serial, long revision, DocumentSnapshot snapshot)
    {
        var diagnostics = new List<Diagnostic>();
        if (serial != snapshot.DocumentRuntimeSerialNumber)
            diagnostics.Add(Error("appearance.document_mismatch", "The active Rhino document changed."));
        if (revision != snapshot.Revision)
            diagnostics.Add(Error("appearance.stale_revision", "The Rhino document changed before this edit was applied."));
        return diagnostics;
    }

    internal static Diagnostic Error(string code, string message) =>
        new(code, DiagnosticSeverity.Error, message);
}

public sealed record SetTemplateCapabilitiesRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    HierarchyScope Source,
    TemplateCapability Capabilities);

public sealed class SetTemplateCapabilitiesPlanner : IOperationPlanner<SetTemplateCapabilitiesRequest>
{
    public OperationPlan Plan(SetTemplateCapabilitiesRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = SetHierarchyViewportRulesPlanner.Context(
            request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        if (!SetHierarchyViewportRulesPlanner.ScopeExists(request.Source, snapshot))
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.source_missing", "The template source no longer exists."));
        var allowed = TemplateCapabilityPolicy.AllowedFor(request.Source.Kind);
        if ((request.Capabilities & ~allowed) != TemplateCapability.None)
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.capability_invalid", "One or more template roles are not valid for this hierarchy item."));

        var existing = snapshot.TemplateRegistrations.LastOrDefault(item => item.Source == request.Source);
        var next = request.Capabilities == TemplateCapability.None
            ? null
            : new CapabilityTemplateRegistration(existing?.Id ?? Guid.NewGuid(), request.Source, request.Capabilities);
        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new SetTemplateCapabilitiesChange(request.Source, existing, next)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            "Update template roles", changes, diagnostics);
    }
}

public sealed record LinkTemplateCapabilityRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    HierarchyScope Target,
    Guid SourceRegistrationId,
    TemplateCapability Capability,
    IReadOnlyList<TemplateDetailMapping>? DetailMappings = null,
    TemplateCapabilityPayload? LastResolved = null);

public sealed class LinkTemplateCapabilityPlanner : IOperationPlanner<LinkTemplateCapabilityRequest>
{
    public OperationPlan Plan(LinkTemplateCapabilityRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = SetHierarchyViewportRulesPlanner.Context(
            request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        if (!SetHierarchyViewportRulesPlanner.ScopeExists(request.Target, snapshot))
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.target_missing", "The template target no longer exists."));
        var registration = snapshot.TemplateRegistrations
            .LastOrDefault(item => item.Id == request.SourceRegistrationId);
        if (registration is null)
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.registration_missing", "The selected template source is unavailable."));
        if (!TemplateCapabilityPolicy.IsSingle(request.Capability))
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.capability_single", "Choose exactly one template capability."));
        if (registration is not null && !registration.Capabilities.HasFlag(request.Capability))
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.capability_unavailable", "The source does not provide that template capability."));

        var existing = snapshot.TemplateLinks.LastOrDefault(link =>
            link.Target == request.Target && link.Capability == request.Capability);
        var next = new CapabilityTemplateLink(
            existing?.Id ?? Guid.NewGuid(),
            request.Target,
            request.SourceRegistrationId,
            request.Capability,
            request.DetailMappings?.ToArray() ?? [],
            request.LastResolved ?? new TemplateCapabilityPayload());
        var registrations = snapshot.TemplateRegistrations.ToDictionary(item => item.Id);
        var proposed = snapshot.TemplateLinks
            .Where(link => link.Id != existing?.Id)
            .Append(next)
            .ToArray();
        if (ViewportAppearanceResolver.HasTemplateCycle(proposed, registrations))
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.link_cycle", "This template link would create a dependency cycle."));

        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new SetCapabilityTemplateLinkChange(request.Target, request.Capability, existing, next)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            "Link template capability", changes, diagnostics);
    }
}

public sealed record DetachTemplateCapabilityRequest(
    uint DocumentRuntimeSerialNumber,
    long SourceRevision,
    HierarchyScope Target,
    TemplateCapability Capability);

public sealed class DetachTemplateCapabilityPlanner : IOperationPlanner<DetachTemplateCapabilityRequest>
{
    public OperationPlan Plan(DetachTemplateCapabilityRequest request, DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(snapshot);
        var diagnostics = SetHierarchyViewportRulesPlanner.Context(
            request.DocumentRuntimeSerialNumber, request.SourceRevision, snapshot);
        var existing = snapshot.TemplateLinks.LastOrDefault(link =>
            link.Target == request.Target && link.Capability == request.Capability);
        if (existing is null)
            diagnostics.Add(SetHierarchyViewportRulesPlanner.Error(
                "template.link_missing", "The selected item is not linked for that capability."));
        IReadOnlyList<OperationChange> changes = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? []
            : [new SetCapabilityTemplateLinkChange(request.Target, request.Capability, existing, null)];
        return new OperationPlan(snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
            "Detach template capability", changes, diagnostics);
    }
}
