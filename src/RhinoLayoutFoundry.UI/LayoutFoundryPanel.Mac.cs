namespace RhinoLayoutFoundry.UI;

public sealed partial class LayoutFoundryPanel
{
    private IDisposable? _nativeClipboardShortcuts;

    partial void AttachNativeClipboardShortcuts()
    {
        DetachNativeClipboardShortcuts();
        _nativeClipboardShortcuts = FoundryNative.Services?.AttachClipboardShortcuts(
            _panelOverlayHost, () => _inlineDraft is null, CopySelection, () => { _ = PasteSelectionAsync(); });
    }

    partial void DetachNativeClipboardShortcuts()
    {
        _nativeClipboardShortcuts?.Dispose();
        _nativeClipboardShortcuts = null;
    }

    partial void RestoreVisibleTreeSelection(IReadOnlyList<HierarchyTreeItem> visibleRows)
    {
        var selectedKeys = _selection.Selected.ToHashSet();
        if (selectedKeys.Count < 2) return;
        var selectedRows = visibleRows.Select((item, index) => (item, index))
            .Where(entry => selectedKeys.Contains(entry.item.Node.Key))
            .Select(entry => entry.index).ToArray();
        if (selectedRows.Length >= 2) FoundryNative.Services?.SelectRows(_treeGrid, selectedRows);
    }
}
