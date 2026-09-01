#if FOUNDRY_MACOS_NATIVE_GESTURES
using AppKit;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class AppearanceRulesTable
{
    partial void ConfigureNativeRowAppearance()
    {
        var nativeView = MacOSHelpers.ToNative(_tree, false);
        if (FindNativeOutlineView(nativeView) is { } outlineView)
            outlineView.UsesAlternatingRowBackgroundColors = true;
    }

    private static NSOutlineView? FindNativeOutlineView(NSView? view)
    {
        if (view is NSOutlineView outlineView) return outlineView;
        if (view is null) return null;
        foreach (var subview in view.Subviews)
            if (FindNativeOutlineView(subview) is { } nested) return nested;
        return null;
    }
}
#endif
