using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryViewIcons
{
    private const int IconSize = 16;

    internal static Bitmap ThumbnailStack()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var muted = FoundryTheme.WithAlpha(color, 145);
        graphics.DrawRectangle(new Pen(muted, 1), 2.5f, 2.5f, 8, 9);
        graphics.DrawRectangle(new Pen(muted, 1), 4.5f, 4.5f, 8, 9);
        graphics.DrawRectangle(new Pen(color, 1.25f), 6.5f, 6.5f, 7, 7);
        graphics.DrawLine(new Pen(color, 1), 8, 11.5f, 12, 11.5f);
        return bitmap;
    }

    internal static Bitmap CartesianPlane()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var grid = FoundryTheme.WithAlpha(color, 85);
        var gridPen = new Pen(grid, 0.75f);
        foreach (var coordinate in new[] { 3.5f, 7.5f, 11.5f })
        {
            graphics.DrawLine(gridPen, 2, coordinate, 14, coordinate);
            graphics.DrawLine(gridPen, coordinate, 2, coordinate, 14);
        }
        var axisPen = new Pen(color, 1.2f);
        graphics.DrawLine(axisPen, 2, 11.5f, 14, 11.5f);
        graphics.DrawLine(axisPen, 4.5f, 14, 4.5f, 2);
        graphics.DrawLine(axisPen, 14, 11.5f, 12, 10);
        graphics.DrawLine(axisPen, 14, 11.5f, 12, 13);
        graphics.DrawLine(axisPen, 4.5f, 2, 3, 4);
        graphics.DrawLine(axisPen, 4.5f, 2, 6, 4);
        graphics.FillEllipse(color, 3.5f, 10.5f, 2, 2);
        return bitmap;
    }

    private static Bitmap NewBitmap() =>
        new(IconSize, IconSize, PixelFormat.Format32bppRgba);
}
