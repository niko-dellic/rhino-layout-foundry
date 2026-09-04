#if FOUNDRY_MACOS_NATIVE_GESTURES
using AppKit;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal static partial class FoundryTable
{
    static partial void ConfigureNativeAppearance(Grid grid)
    {
        if (FindTable(MacOSHelpers.ToNative(grid, false)) is { } table)
            // AppKit paints striping beneath its live row selection. Opaque Eto
            // cell backgrounds cover that selection during native mouse tracking.
            table.UsesAlternatingRowBackgroundColors = true;
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
