#if FOUNDRY_MACOS_NATIVE_GESTURES
using AppKit;
using Eto.Forms;
using Foundation;

namespace RhinoLayoutFoundry.UI;

public sealed partial class LayoutFoundryPanel
{
    partial void RestoreVisibleTreeSelection(IReadOnlyList<HierarchyTreeItem> visibleRows)
    {
        var selectedKeys = _selection.Selected.ToHashSet();
        if (selectedKeys.Count < 2)
            return;

        var selectedRows = visibleRows
            .Select((item, index) => (item, index))
            .Where(entry => selectedKeys.Contains(entry.item.Node.Key))
            .Select(entry => (nuint)entry.index)
            .ToArray();
        if (selectedRows.Length < 2)
            return;

        var nativeView = MacOSHelpers.ToNative(_treeGrid, false);
        var outlineView = FindOutlineView(nativeView);
        if (outlineView is null)
            return;

        using var indexes = NSIndexSet.FromArray(selectedRows);
        outlineView.SelectRows(indexes, false);
    }

    private static NSOutlineView? FindOutlineView(NSView? view)
    {
        if (view is NSOutlineView outlineView)
            return outlineView;
        if (view is null)
            return null;

        foreach (var subview in view.Subviews)
        {
            if (FindOutlineView(subview) is { } nested)
                return nested;
        }

        return null;
    }
}
#endif
