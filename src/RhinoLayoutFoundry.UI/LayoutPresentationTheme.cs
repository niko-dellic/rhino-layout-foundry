using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

internal static class LayoutPresentationTheme
{
    internal static Color SheetPaper => Colors.White;
    internal static Color SheetShadow => Color.FromArgb(48, 0, 0, 0);
    internal static Color SheetDetailPlaceholder => Color.FromArgb(255, 242, 243, 244);
    internal static Color SheetDetailBorder => Color.FromArgb(130, 100, 103, 106);
    internal static Color SheetOutline => Color.FromArgb(125, 90, 90, 90);
    internal static Color SheetPrintIncluded => Color.FromArgb(255, 245, 188, 32);
    internal static Color SheetPrintExcluded => Color.FromArgb(255, 95, 95, 95);
    internal static Color CanvasPreviewBackground => Colors.White;
}
