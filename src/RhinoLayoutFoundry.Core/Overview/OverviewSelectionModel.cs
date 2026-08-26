namespace RhinoLayoutFoundry.Core.Overview;

public sealed class OverviewSelectionModel
{
    private readonly HashSet<OverviewNodeKey> _selected = [];

    public OverviewNodeKey? Anchor { get; private set; }

    public IReadOnlySet<OverviewNodeKey> Selected => _selected;

    public void Replace(IEnumerable<OverviewNodeKey> keys, OverviewNodeKey? anchor = null)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _selected.Clear();
        _selected.UnionWith(keys);
        Anchor = anchor is { } candidate && _selected.Contains(candidate)
            ? candidate
            : _selected.Count == 1
                ? _selected.Single()
                : null;
    }

    public void Toggle(OverviewNodeKey key)
    {
        if (!_selected.Add(key))
        {
            _selected.Remove(key);
        }

        Anchor = _selected.Contains(key) ? key : _selected.FirstOrDefault();
        if (_selected.Count == 0)
        {
            Anchor = null;
        }
    }

    public void SelectRange(
        IReadOnlyList<OverviewNodeKey> visibleOrder,
        OverviewNodeKey target,
        bool additive)
    {
        ArgumentNullException.ThrowIfNull(visibleOrder);

        var targetIndex = IndexOf(visibleOrder, target);
        if (targetIndex < 0)
        {
            return;
        }

        var anchor = Anchor is { } currentAnchor && IndexOf(visibleOrder, currentAnchor) >= 0
            ? currentAnchor
            : target;
        var anchorIndex = IndexOf(visibleOrder, anchor);
        if (!additive)
        {
            _selected.Clear();
        }

        var start = Math.Min(anchorIndex, targetIndex);
        var end = Math.Max(anchorIndex, targetIndex);
        for (var index = start; index <= end; index++)
        {
            _selected.Add(visibleOrder[index]);
        }

        Anchor = anchor;
    }

    public void Prune(IEnumerable<OverviewNodeKey> existingKeys)
    {
        ArgumentNullException.ThrowIfNull(existingKeys);

        _selected.IntersectWith(existingKeys);
        if (Anchor is { } anchor && !_selected.Contains(anchor))
        {
            Anchor = _selected.Count == 1 ? _selected.Single() : null;
        }
    }

    public IReadOnlyList<OverviewNodeKey> VisibleSelection(
        IEnumerable<OverviewNodeKey> visibleKeys)
    {
        ArgumentNullException.ThrowIfNull(visibleKeys);
        return visibleKeys.Where(_selected.Contains).ToArray();
    }

    public void Clear()
    {
        _selected.Clear();
        Anchor = null;
    }

    private static int IndexOf(
        IReadOnlyList<OverviewNodeKey> values,
        OverviewNodeKey target)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == target)
            {
                return index;
            }
        }

        return -1;
    }
}
