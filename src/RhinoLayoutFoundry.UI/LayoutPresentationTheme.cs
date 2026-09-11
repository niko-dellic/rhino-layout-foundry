using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

internal static class LayoutPresentationTheme
{
    internal static Color SelectionAccent => FoundryTheme.IsDarkMode
        ? Color.FromArgb(68, 68, 68)
        : Color.FromArgb(219, 219, 219);
    // A contrasting keyline keeps neutral selections visible against both
    // white paper and the board. Widths are screen pixels, independent of zoom.
    internal static void DrawSelectionKeyline(Graphics graphics, RectangleF bounds, float width)
    {
        using var pen = new Pen(SelectionForeground, width + 3);
        graphics.DrawRectangle(pen, bounds);
    }

    internal static Color SelectionForeground => FoundryTable.SelectionForeground(SelectionAccent);
    internal static Color SelectionFill => FoundryTheme.WithAlpha(SelectionAccent, 40);
    internal static Color CanvasLabelHalo => FoundryTheme.WithAlpha(FoundryTheme.CanvasBackground, 185);
    internal static Color CanvasConnectorHalo => FoundryTheme.WithAlpha(FoundryTheme.CanvasBackground, 190);
    internal static Color SheetPaper => Colors.White;
    internal static Color SheetShadow => Color.FromArgb(48, 0, 0, 0);
    internal static Color SheetDetailPlaceholder => Color.FromArgb(255, 242, 243, 244);
    internal static Color SheetDetailBorder => Color.FromArgb(130, 100, 103, 106);
    internal static Color SheetOutline => Color.FromArgb(125, 90, 90, 90);
    internal static Color SheetPrintIncluded => Color.FromArgb(255, 245, 188, 32);
    internal static Color SheetPrintExcluded => Color.FromArgb(255, 95, 95, 95);
    internal static Color CanvasPreviewBackground => Colors.White;
}
