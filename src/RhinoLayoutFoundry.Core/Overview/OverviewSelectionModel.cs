using RhinoFoundry.UI.Primitives;

namespace RhinoLayoutFoundry.Core.Overview;

/// <summary>Product identity adapter over the shared selection rules.</summary>
public sealed class OverviewSelectionModel
{
    private readonly FoundrySelectionModel<OverviewNodeKey> _selection = new();
    public OverviewNodeKey? Anchor => _selection.Anchor;
    public IReadOnlySet<OverviewNodeKey> Selected => _selection.Selected;
    public void Replace(IEnumerable<OverviewNodeKey> keys, OverviewNodeKey? anchor = null) => _selection.Replace(keys, anchor);
    public void Toggle(OverviewNodeKey key) => _selection.Toggle(key);
    public void SelectRange(IReadOnlyList<OverviewNodeKey> visibleOrder, OverviewNodeKey target, bool additive) =>
        _selection.SelectRange(visibleOrder, target, additive);
    public void Prune(IEnumerable<OverviewNodeKey> existingKeys) => _selection.Prune(existingKeys);
    public IReadOnlyList<OverviewNodeKey> VisibleSelection(IEnumerable<OverviewNodeKey> visibleKeys) => _selection.VisibleSelection(visibleKeys);
    public void Clear() => _selection.Clear();
}
