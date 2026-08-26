using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Hierarchy;

namespace RhinoLayoutFoundry.Core.Rules;

public sealed record DisplayRuleResolution(
    IReadOnlyDictionary<ObjectDetailKey, Guid> Overrides,
    IReadOnlyList<Diagnostic> Diagnostics);

public static class DisplayRuleResolver
{
    public static DisplayRuleResolution Resolve(
        DocumentSnapshot snapshot,
        IEnumerable<DisplayRule> rules)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(rules);

        var hierarchy = new HierarchyIndex(snapshot);
        var overrides = new Dictionary<ObjectDetailKey, Guid>();
        var diagnostics = new List<Diagnostic>();

        var orderedRules = rules
            .Select((rule, index) => (Rule: rule, Index: index))
            .OrderBy(item => item.Rule.Priority)
            .ThenBy(item => item.Index);

        foreach (var (rule, _) in orderedRules)
        {
            if (!rule.Enabled)
            {
                continue;
            }

            if (!snapshot.DisplayModeIds.Contains(rule.DisplayModeId))
            {
                diagnostics.Add(new Diagnostic(
                    "RULE_DISPLAY_MODE_MISSING",
                    DiagnosticSeverity.Error,
                    $"Display rule '{rule.Name}' references a missing display mode.",
                    rule.Id));
                continue;
            }

            var detailIds = new HashSet<Guid>();
            foreach (var target in rule.Targets)
            {
                if (hierarchy.TryResolveDetails(target, out var resolved))
                {
                    detailIds.UnionWith(resolved);
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        "RULE_TARGET_MISSING",
                        DiagnosticSeverity.Warning,
                        $"Display rule '{rule.Name}' contains a missing {target.Kind} target '{target.Id}'.",
                        rule.Id));
                }
            }

            foreach (var objectId in rule.ObjectIds.Distinct())
            {
                if (!snapshot.ExistingObjectIds.Contains(objectId))
                {
                    diagnostics.Add(new Diagnostic(
                        "RULE_OBJECT_MISSING",
                        DiagnosticSeverity.Warning,
                        $"Display rule '{rule.Name}' references missing object '{objectId}'.",
                        rule.Id));
                    continue;
                }

                foreach (var detailId in detailIds)
                {
                    overrides[new ObjectDetailKey(objectId, detailId)] = rule.DisplayModeId;
                }
            }
        }

        return new DisplayRuleResolution(overrides, diagnostics);
    }
}

