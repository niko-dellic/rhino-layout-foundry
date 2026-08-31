namespace RhinoLayoutFoundry.Core.Overview;

public sealed class HierarchyExpansionState
{
    private readonly HashSet<OverviewNodeKey> _expanded = [];
    private readonly HashSet<OverviewNodeKey> _collapsed = [];

    public void Record(OverviewNodeKey key, bool expanded)
    {
        if (expanded)
        {
            _collapsed.Remove(key);
            _expanded.Add(key);
        }
        else
        {
            _expanded.Remove(key);
            _collapsed.Add(key);
        }
    }

    public bool Resolve(
        OverviewNodeKey key,
        bool expandedByDefault,
        bool forceExpanded = false,
        bool containsPreferredSelection = false) =>
        forceExpanded ||
        containsPreferredSelection ||
        _expanded.Contains(key) ||
        expandedByDefault && !_collapsed.Contains(key);

    public void Prune(IEnumerable<OverviewNodeKey> existingKeys)
    {
        var retained = existingKeys.ToHashSet();
        _expanded.IntersectWith(retained);
        _collapsed.IntersectWith(retained);
    }

    public void Clear()
    {
        _expanded.Clear();
        _collapsed.Clear();
    }
}
