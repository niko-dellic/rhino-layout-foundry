#if FOUNDRY_MACOS_NATIVE_GESTURES
using AppKit;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal static partial class FoundryTable
{
    static partial void ConfigureNativeAppearance(Grid grid)
    {
        if (FindTable(MacOSHelpers.ToNative(grid, false)) is { } table)
            // Foundry formats populated rows consistently on both platforms.
            // AppKit's own striping also paints empty space below tree rows.
            table.UsesAlternatingRowBackgroundColors = false;
    }

    private static NSTableView? FindTable(NSView? view)
    {
        if (view is NSTableView table) return table;
        if (view is null) return null;
        foreach (var child in view.Subviews)
            if (FindTable(child) is { } nested) return nested;
        return null;
    }
}
#endif
