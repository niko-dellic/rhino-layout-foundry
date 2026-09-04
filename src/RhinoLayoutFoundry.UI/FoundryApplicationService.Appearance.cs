using Eto.Drawing;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class FoundryApplicationService
{
    public async Task<OperationResult> CaptureSheetTemplateAsync(
        Guid sourcePageViewId,
        string name,
        string defaultNamingPattern,
        Guid? titleBlockInstanceObjectId,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new CaptureSheetTemplatePlanner().Plan(new CaptureSheetTemplateRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                Guid.NewGuid(),
                sourcePageViewId,
                name,
                defaultNamingPattern,
                titleBlockInstanceObjectId), snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata | OverviewInvalidationKind.Diagnostics));
            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> SetSheetTemplateRegistrationAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        bool registered,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");

        try
        {
            var snapshot = _snapshotProvider.Capture();
            var sheetIds = BatchTargetResolver.ResolveSheetIds(snapshot, targets);
            if (sheetIds.Count == 0)
                return UnavailableResult("The selected rows do not contain any layouts.");

            var diagnostics = new List<Diagnostic>();
            foreach (var sheetId in sheetIds)
            {
                var result = await SetSheetTemplateRegistrationAsync(
                    sheetId,
                    registered,
                    cancellationToken);
                diagnostics.AddRange(result.Diagnostics);
                if (!result.Succeeded)
                    return new OperationResult(false, diagnostics);
            }

            return new OperationResult(true, diagnostics);
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public async Task<OperationResult> SetTemplateCapabilitiesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        TemplateCapability capabilities,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        foreach (var target in targets.Distinct())
        {
            try
            {
                var snapshot = _snapshotProvider.Capture();
                var scope = ToHierarchyScope(target);
                var plan = new SetTemplateCapabilitiesPlanner().Plan(
                    new SetTemplateCapabilitiesRequest(
                        snapshot.DocumentRuntimeSerialNumber,
                        snapshot.Revision,
                        scope,
                        capabilities),
                    snapshot);
                var result = plan.CanApply
                    ? await Mutations.ApplyAsync(plan, cancellationToken)
                    : new OperationResult(false, plan.Diagnostics);
                diagnostics.AddRange(result.Diagnostics);
                if (!result.Succeeded) return new OperationResult(false, diagnostics);
            }
            catch (InvalidOperationException exception)
            {
                return UnavailableResult(exception.Message);
            }
        }
        NotifyOverviewChanged(new OverviewInvalidation(null,
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics |
            OverviewInvalidationKind.Thumbnails));
        return new OperationResult(true, diagnostics);
    }

    public async Task<OperationResult> SetLayerVisibilityRulesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        IReadOnlyList<Guid> layerIds,
        LayerVisibilityOverride? visibility,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        foreach (var target in targets.Distinct())
        {
            var snapshot = _snapshotProvider.Capture();
            var scope = ToHierarchyScope(target);
            var current = snapshot.AppearanceRules.LastOrDefault(item => item.Scope == scope);
            var selected = layerIds.ToHashSet();
            var rules = (current?.LayerRules ?? [])
                .Where(rule => !selected.Contains(rule.Layer.LayerId))
                .ToList();
            if (visibility is { } nextVisibility)
            {
                foreach (var layerId in selected)
                {
                    if (!snapshot.LayerSnapshots.TryGetValue(layerId, out var layer))
                        return UnavailableResult("A selected Rhino layer is no longer available.");
                    rules.Add(new LayerVisibilityRule(
                        new LayerReference(layer.Id, layer.FullPath),
                        nextVisibility));
                }
            }
            var plan = new SetHierarchyViewportRulesPlanner().Plan(
                new SetHierarchyViewportRulesRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    scope,
                    rules,
                    null),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded) return new OperationResult(false, diagnostics);
        }
        NotifyOverviewChanged(OverviewInvalidation.All);
        return new OperationResult(true, diagnostics);
    }

    public async Task<OperationResult> SetObjectDisplayRulesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        IReadOnlyList<ObjectDisplayRule> rules,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        foreach (var target in targets.Distinct())
        {
            var snapshot = _snapshotProvider.Capture();
            var scope = ToHierarchyScope(target);
            var plan = new SetHierarchyViewportRulesPlanner().Plan(
                new SetHierarchyViewportRulesRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    scope,
                    null,
                    rules),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded) return new OperationResult(false, diagnostics);
        }
        NotifyOverviewChanged(OverviewInvalidation.All);
        return new OperationResult(true, diagnostics);
    }

    public async Task<OperationResult> SetAppearanceRulesAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        IReadOnlyList<LayerVisibilityRule> layerRules,
        IReadOnlyList<ObjectDisplayRule> objectRules,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        foreach (var target in targets.Distinct())
        {
            var snapshot = _snapshotProvider.Capture();
            var plan = new SetHierarchyViewportRulesPlanner().Plan(
                new SetHierarchyViewportRulesRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    ToHierarchyScope(target),
                    layerRules,
                    objectRules),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded) return new OperationResult(false, diagnostics);
        }
        NotifyOverviewChanged(OverviewInvalidation.All);
        return new OperationResult(true, diagnostics);
    }

    public async Task<OperationResult> CreateAppearanceStateAsync(
        Guid folderId,
        string name,
        IReadOnlyList<LayerVisibilityRule>? layerRules = null,
        IReadOnlyList<ObjectDisplayRule>? objectRules = null,
        string notes = "",
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        try
        {
            var snapshot = _snapshotProvider.Capture();
            var plan = new CreateAppearanceStatePlanner().Plan(new CreateAppearanceStateRequest(
                snapshot.DocumentRuntimeSerialNumber,
                snapshot.Revision,
                folderId,
                name,
                layerRules,
                objectRules,
                notes), snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded) NotifyOverviewChanged(OverviewInvalidation.All);
            return result;
        }
        catch (Exception exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public async Task<OperationResult> UpdateAppearanceStateAsync(
        Guid stateId,
        string? name = null,
        IReadOnlyList<LayerVisibilityRule>? layerRules = null,
        IReadOnlyList<ObjectDisplayRule>? objectRules = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var plan = new UpdateAppearanceStatePlanner().Plan(new UpdateAppearanceStateRequest(
                snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, stateId,
                Name: name, LayerRules: layerRules, ObjectDisplayRules: objectRules, Notes: notes), snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded) NotifyOverviewChanged(OverviewInvalidation.All);
            return result;
        }, cancellationToken);
    }

    public async Task<OperationResult> AssignAppearanceStateAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid? stateId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        try
        {
            foreach (var target in targets.Distinct())
            {
                var snapshot = _snapshotProvider.Capture();
                var plan = new AssignAppearanceStatePlanner().Plan(new AssignAppearanceStateRequest(
                    snapshot.DocumentRuntimeSerialNumber, snapshot.Revision,
                    ToHierarchyScope(target), stateId), snapshot);
                var result = plan.CanApply
                    ? await Mutations.ApplyAsync(plan, cancellationToken)
                    : new OperationResult(false, plan.Diagnostics);
                diagnostics.AddRange(result.Diagnostics);
                if (!result.Succeeded) return new OperationResult(false, diagnostics);
            }
            NotifyOverviewChanged(OverviewInvalidation.All);
            return new OperationResult(true, diagnostics);
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public ModelObjectSelectionResult PickModelObjects()
    {
        try
        {
            return _modelObjectSelectionService?.PickObjects() ??
                   new ModelObjectSelectionResult(
                       false, false, [], "Foundry is not connected to Rhino object selection.");
        }
        catch (Exception exception)
        {
            return new ModelObjectSelectionResult(false, false, [], exception.Message);
        }
    }

    public async Task<OperationResult> MoveAppearanceStatesAsync(
        IReadOnlyList<Guid> stateIds,
        Guid destinationFolderId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        try
        {
            foreach (var stateId in stateIds.Distinct())
            {
                var snapshot = _snapshotProvider.Capture();
                var plan = new UpdateAppearanceStatePlanner().Plan(new UpdateAppearanceStateRequest(
                    snapshot.DocumentRuntimeSerialNumber, snapshot.Revision, stateId,
                    FolderId: destinationFolderId), snapshot);
                var result = plan.CanApply
                    ? await Mutations.ApplyAsync(plan, cancellationToken)
                    : new OperationResult(false, plan.Diagnostics);
                diagnostics.AddRange(result.Diagnostics);
                if (!result.Succeeded) return new OperationResult(false, diagnostics);
            }
            NotifyOverviewChanged(OverviewInvalidation.All);
            return new OperationResult(true, diagnostics);
        }
        catch (InvalidOperationException exception)
        {
            return UnavailableResult(exception.Message);
        }
    }

    public async Task<OperationResult> LinkTemplateCapabilityAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid sourceRegistrationId,
        TemplateCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        foreach (var target in targets.Distinct())
        {
            var snapshot = _snapshotProvider.Capture();
            var registration = snapshot.TemplateRegistrations
                .LastOrDefault(item => item.Id == sourceRegistrationId);
            if (registration is null)
                return UnavailableResult("The selected template source is no longer available.");
            var plan = new LinkTemplateCapabilityPlanner().Plan(
                new LinkTemplateCapabilityRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    ToHierarchyScope(target),
                    sourceRegistrationId,
                    capability),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded) return new OperationResult(false, diagnostics);
        }
        NotifyOverviewChanged(OverviewInvalidation.All);
        return new OperationResult(true, diagnostics);
    }

    public async Task<OperationResult> DetachTemplateCapabilityAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        TemplateCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (_snapshotProvider is null || _mutationService is null)
            return UnavailableResult("Foundry is not connected to an active Rhino plug-in.");
        var diagnostics = new List<Diagnostic>();
        foreach (var target in targets.Distinct())
        {
            var snapshot = _snapshotProvider.Capture();
            var scope = ToHierarchyScope(target);
            if (!snapshot.TemplateLinks.Any(link =>
                    link.Target == scope && link.Capability == capability))
                continue;
            var plan = new DetachTemplateCapabilityPlanner().Plan(
                new DetachTemplateCapabilityRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    scope,
                    capability),
                snapshot);
            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            diagnostics.AddRange(result.Diagnostics);
            if (!result.Succeeded) return new OperationResult(false, diagnostics);
        }
        NotifyOverviewChanged(OverviewInvalidation.All);
        return new OperationResult(true, diagnostics);
    }

    internal HierarchyScope ToHierarchyScope(OverviewNodeKey key) => new(
        key.Kind switch
        {
            OverviewNodeKind.Folder => HierarchyScopeKind.Folder,
            OverviewNodeKind.Sheet => HierarchyScopeKind.Sheet,
            OverviewNodeKind.Detail => HierarchyScopeKind.Detail,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        },
        key.Id);

    public async Task<OperationResult> SetSheetTemplateRegistrationAsync(
        Guid sourcePageViewId,
        bool registered,
        CancellationToken cancellationToken = default)
    {
        return await RunOperationAsync(async snapshot =>
        {
            var existing = snapshot.Templates
                .Where(template => template.SourcePageViewId == sourcePageViewId)
                .ToArray();
            if (registered == (existing.Length > 0))
                return new OperationResult(true, []);

            OperationPlan plan;
            if (registered)
            {
                if (!snapshot.Sheets.TryGetValue(sourcePageViewId, out var sheet))
                    return UnavailableResult("The layout is no longer available.");

                var usedNames = snapshot.Templates.Select(template => template.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var name = UniqueTemplateName(sheet.Name, usedNames);
                plan = new CaptureSheetTemplatePlanner().Plan(new CaptureSheetTemplateRequest(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    Guid.NewGuid(),
                    sourcePageViewId,
                    name,
                    "{folder}-{index:00}",
                    sheet.TitleBlockInstanceObjectId), snapshot);
            }
            else
            {
                plan = new OperationPlan(
                    snapshot.DocumentRuntimeSerialNumber,
                    snapshot.Revision,
                    "Unregister layout template",
                    existing.Select(template => (OperationChange)new DeleteSheetTemplateChange(
                        template.Id,
                        template.Name)).ToArray(),
                    []);
            }

            var result = plan.CanApply
                ? await Mutations.ApplyAsync(plan, cancellationToken)
                : new OperationResult(false, plan.Diagnostics);
            if (result.Succeeded)
                NotifyOverviewChanged(new OverviewInvalidation(snapshot.DocumentRuntimeSerialNumber,
                    OverviewInvalidationKind.Metadata | OverviewInvalidationKind.Diagnostics));
            return result;
        }, cancellationToken);
    }

    private string UniqueTemplateName(string sheetName, IReadOnlySet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(sheetName) ? "Layout template" : sheetName.Trim();
        if (!usedNames.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!usedNames.Contains(candidate)) return candidate;
        }
    }

}
