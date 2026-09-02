#if FOUNDRY_MACOS_NATIVE_GESTURES
using AppKit;
using Eto.Forms;
using Foundation;

namespace RhinoLayoutFoundry.UI;

public sealed partial class LayoutFoundryPanel
{
    private NSView? _nativePanelContentView;
    private NSObject? _nativeClipboardShortcutMonitor;

    partial void AttachNativeClipboardShortcuts()
    {
        DetachNativeClipboardShortcuts();
        _nativePanelContentView = MacOSHelpers.ToNative(_panelOverlayHost, false);
        if (_nativePanelContentView is null)
            return;

        _nativeClipboardShortcutMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
            NSEventMask.KeyDown,
            HandleNativeClipboardShortcut);
    }

    partial void DetachNativeClipboardShortcuts()
    {
        if (_nativeClipboardShortcutMonitor is not null)
        {
            NSEvent.RemoveMonitor(_nativeClipboardShortcutMonitor);
            _nativeClipboardShortcutMonitor.Dispose();
            _nativeClipboardShortcutMonitor = null;
        }

        _nativePanelContentView = null;
    }

    private NSEvent HandleNativeClipboardShortcut(NSEvent nativeEvent)
    {
        if (!IsFoundryClipboardShortcutTarget(nativeEvent) ||
            !TryGetClipboardShortcut(nativeEvent, out var copy))
            return nativeEvent;

        if (copy)
            CopySelection();
        else
            _ = PasteSelectionAsync();

        // AppKit otherwise routes Command-C/V through Rhino's application menu
        // before Eto's focused-control KeyDown event can handle the shortcut.
        return null!;
    }

    private bool IsFoundryClipboardShortcutTarget(NSEvent nativeEvent)
    {
        if (_nativePanelContentView is null ||
            nativeEvent.Window is null ||
            nativeEvent.Window != _nativePanelContentView.Window ||
            _inlineDraft is not null ||
            nativeEvent.Window.FirstResponder is not NSView responderView)
            return false;

        // Native text editors must retain their standard clipboard behavior.
        if (responderView is NSTextView or NSTextField)
            return false;

        for (var view = responderView; view is not null; view = view.Superview)
            if (ReferenceEquals(view, _nativePanelContentView))
                return true;

        return false;
    }

    private static bool TryGetClipboardShortcut(NSEvent nativeEvent, out bool copy)
    {
        copy = false;
        var modifiers = nativeEvent.ModifierFlags;
        var primary = modifiers.HasFlag(NSEventModifierMask.CommandKeyMask) ||
                      modifiers.HasFlag(NSEventModifierMask.ControlKeyMask);
        if (!primary ||
            modifiers.HasFlag(NSEventModifierMask.AlternateKeyMask) ||
            modifiers.HasFlag(NSEventModifierMask.ShiftKeyMask))
            return false;

        var character = nativeEvent.CharactersIgnoringModifiers;
        if (string.Equals(character, "c", StringComparison.OrdinalIgnoreCase))
        {
            copy = true;
            return true;
        }

        return string.Equals(character, "v", StringComparison.OrdinalIgnoreCase);
    }

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
#else
namespace RhinoLayoutFoundry.UI;

public sealed partial class LayoutFoundryPanel
{
    partial void AttachNativeClipboardShortcuts()
    {
    }

    partial void DetachNativeClipboardShortcuts()
    {
    }
}
#endif
