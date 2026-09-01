namespace RhinoLayoutFoundry.Core.Domain;

[Flags]
public enum TemplateCapability
{
    None = 0,
    Layout = 1 << 0,
    TitleBlock = 1 << 1,
}

public enum HierarchyScopeKind
{
    Folder,
    Sheet,
    Detail,
}

public readonly record struct HierarchyScope(HierarchyScopeKind Kind, Guid Id);

public enum LayerVisibilityOverride
{
    Visible,
    Hidden,
}

public sealed record LayerReference(
    Guid LayerId,
    string FullPath);

public sealed record LayerVisibilityRule(
    LayerReference Layer,
    LayerVisibilityOverride Visibility);

public enum ObjectDisplaySelectorKind
{
    ExactObject,
    Layer,
}

public sealed record ObjectDisplaySelector(
    ObjectDisplaySelectorKind Kind,
    Guid? ObjectId = null,
    Guid? LayerId = null,
    string? LayerFullPath = null);

public sealed record ObjectDisplayRule(
    ObjectDisplaySelector Selector,
    Guid DisplayModeId,
    string DisplayModeName);

/// <summary>
/// Local rules declared at one hierarchy scope. Missing rules inherit from the
/// next less-specific scope and ultimately from Rhino's native viewport state.
/// </summary>
public sealed record HierarchyViewportRuleSet(
    HierarchyScope Scope,
    IReadOnlyList<LayerVisibilityRule> LayerRules,
    IReadOnlyList<ObjectDisplayRule> ObjectDisplayRules);

/// <summary>
/// A reusable, document-owned appearance resource. Folder membership is only
/// organizational; behavior is established through explicit assignments.
/// </summary>
public sealed record AppearanceStateRecord(
    Guid Id,
    Guid FolderId,
    int Order,
    string Name,
    IReadOnlyList<LayerVisibilityRule> LayerRules,
    IReadOnlyList<ObjectDisplayRule> ObjectDisplayRules,
    string Notes = "");

/// <summary>
/// A target can own at most one appearance-state assignment.
/// More-specific hierarchy assignments naturally replace the inherited basis.
/// </summary>
public sealed record AppearanceStateAssignment(
    Guid Id,
    HierarchyScope Target,
    Guid StateId);

public sealed record CapabilityTemplateRegistration(
    Guid Id,
    HierarchyScope Source,
    TemplateCapability Capabilities);

public sealed record TemplateDetailMapping(
    Guid SourceDetailViewportId,
    Guid TargetDetailViewportId);

public sealed record TemplateCapabilityPayload(
    SheetTemplateRecipe? Layout = null,
    TitleBlockTemplateRecipe? TitleBlock = null);

/// <summary>
/// One live capability link. LastResolved is deliberately persisted so source
/// loss can detach without changing the target's visible result.
/// </summary>
public sealed record CapabilityTemplateLink(
    Guid Id,
    HierarchyScope Target,
    Guid SourceRegistrationId,
    TemplateCapability Capability,
    IReadOnlyList<TemplateDetailMapping> DetailMappings,
    TemplateCapabilityPayload LastResolved);

public sealed record LayerSnapshot(
    Guid Id,
    Guid? ParentId,
    string FullPath,
    bool IsGloballyVisible);

public sealed record DetailLayerVisibilitySnapshot(
    Guid DetailViewportId,
    Guid LayerId,
    bool IsVisible,
    bool HasExplicitOverride);

public sealed record DetailObjectDisplayOverrideSnapshot(
    Guid DetailViewportId,
    Guid ObjectId,
    Guid DisplayModeId,
    string DisplayModeName);

public sealed record ModelObjectSnapshot(
    Guid Id,
    string Name,
    Guid LayerId,
    string LayerFullPath,
    bool IsInstanceObject);

public sealed record EffectiveViewportAppearance(
    IReadOnlyDictionary<Guid, LayerVisibilityOverride> Layers,
    IReadOnlyDictionary<Guid, ObjectDisplayRule> Objects);

public static class TemplateCapabilityPolicy
{
    public static TemplateCapability AllowedFor(HierarchyScopeKind kind) => kind switch
    {
        HierarchyScopeKind.Folder => TemplateCapability.Layout,
        HierarchyScopeKind.Sheet =>
            TemplateCapability.Layout |
            TemplateCapability.TitleBlock,
        HierarchyScopeKind.Detail => TemplateCapability.Layout,
        _ => TemplateCapability.None,
    };

    public static bool IsSingle(TemplateCapability capability) =>
        capability != TemplateCapability.None &&
        ((int)capability & ((int)capability - 1)) == 0;
}

public static class ViewportAppearanceResolver
{
    public static EffectiveViewportAppearance Resolve(
        IEnumerable<HierarchyScope> leastToMostSpecificScopes,
        IReadOnlyDictionary<HierarchyScope, HierarchyViewportRuleSet> localRules,
        IReadOnlyDictionary<Guid, LayerSnapshot> layers,
        IReadOnlyDictionary<Guid, ModelObjectSnapshot> objects,
        IReadOnlyDictionary<Guid, AppearanceStateRecord>? appearanceStates = null,
        IReadOnlyList<AppearanceStateAssignment>? stateAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(leastToMostSpecificScopes);
        ArgumentNullException.ThrowIfNull(localRules);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(objects);

        var resolvedLayers = new Dictionary<Guid, LayerVisibilityOverride>();
        var exactObjects = new Dictionary<Guid, ObjectDisplayRule>();
        var layerSelectors = new List<ObjectDisplayRule>();

        void ApplyRules(
            IReadOnlyList<LayerVisibilityRule> layerRules,
            IReadOnlyList<ObjectDisplayRule> objectRules)
        {
            foreach (var rule in layerRules)
                if (layers.ContainsKey(rule.Layer.LayerId))
                    resolvedLayers[rule.Layer.LayerId] = rule.Visibility;
            foreach (var rule in objectRules)
            {
                if (rule.Selector.Kind == ObjectDisplaySelectorKind.ExactObject &&
                    rule.Selector.ObjectId is { } objectId)
                    exactObjects[objectId] = rule;
                else if (rule.Selector.Kind == ObjectDisplaySelectorKind.Layer)
                    layerSelectors.Add(rule);
            }
        }

        void ApplyAssigned(HierarchyScope target)
        {
            if (appearanceStates is null || stateAssignments is null) return;
            var assignment = stateAssignments.LastOrDefault(item => item.Target == target);
            if (assignment is null ||
                !appearanceStates.TryGetValue(assignment.StateId, out var state))
                return;
            ApplyRules(state.LayerRules, state.ObjectDisplayRules);
        }

        foreach (var scope in leastToMostSpecificScopes)
        {
            ApplyAssigned(scope);
            if (!localRules.TryGetValue(scope, out var rules)) continue;
            ApplyRules(rules.LayerRules, rules.ObjectDisplayRules);
        }

        var resolvedObjects = new Dictionary<Guid, ObjectDisplayRule>();
        foreach (var modelObject in objects.Values)
        {
            ObjectDisplayRule? selected = null;
            var selectedDepth = -1;
            foreach (var rule in layerSelectors)
            {
                if (!MatchesLayer(rule.Selector, modelObject.LayerId, layers, out var depth) ||
                    depth < selectedDepth)
                    continue;
                selected = rule;
                selectedDepth = depth;
            }
            if (selected is not null) resolvedObjects[modelObject.Id] = selected;
        }
        foreach (var pair in exactObjects)
            if (objects.ContainsKey(pair.Key)) resolvedObjects[pair.Key] = pair.Value;

        return new EffectiveViewportAppearance(resolvedLayers, resolvedObjects);
    }

    public static bool HasTemplateCycle(
        IEnumerable<CapabilityTemplateLink> links,
        IReadOnlyDictionary<Guid, CapabilityTemplateRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(registrations);
        var sourceByTarget = links
            .Where(link => registrations.ContainsKey(link.SourceRegistrationId))
            .GroupBy(link => (link.Target, link.Capability))
            .ToDictionary(
                group => group.Key,
                group => registrations[group.Last().SourceRegistrationId].Source);
        var states = new Dictionary<(HierarchyScope Scope, TemplateCapability Capability), int>();

        bool Visit((HierarchyScope Scope, TemplateCapability Capability) node)
        {
            if (states.GetValueOrDefault(node) == 1) return true;
            if (states.GetValueOrDefault(node) == 2) return false;
            states[node] = 1;
            if (sourceByTarget.TryGetValue(node, out var source) && Visit((source, node.Capability)))
                return true;
            states[node] = 2;
            return false;
        }

        return sourceByTarget.Keys.Any(Visit);
    }

    private static bool MatchesLayer(
        ObjectDisplaySelector selector,
        Guid objectLayerId,
        IReadOnlyDictionary<Guid, LayerSnapshot> layers,
        out int depth)
    {
        depth = -1;
        if (selector.LayerId is not { } selectorLayerId || !layers.ContainsKey(selectorLayerId))
            return false;
        var current = objectLayerId;
        var currentDepth = 0;
        while (layers.TryGetValue(current, out var layer))
        {
            if (current == selectorLayerId)
            {
                depth = LayerDepth(selectorLayerId, layers);
                return true;
            }
            if (layer.ParentId is not { } parentId) break;
            current = parentId;
            currentDepth++;
        }
        return false;
    }

    private static int LayerDepth(Guid layerId, IReadOnlyDictionary<Guid, LayerSnapshot> layers)
    {
        var depth = 0;
        var current = layerId;
        var visited = new HashSet<Guid>();
        while (visited.Add(current) && layers.TryGetValue(current, out var layer) &&
               layer.ParentId is { } parentId)
        {
            depth++;
            current = parentId;
        }
        return depth;
    }
}
