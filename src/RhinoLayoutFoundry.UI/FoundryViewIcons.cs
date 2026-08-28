using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryViewIcons
{
    private const int IconSize = 16;
    private const float Hairline = 0.8f;
    private const float Emphasis = 0.9f;

    internal static Bitmap ThumbnailStack()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var muted = FoundryTheme.WithAlpha(color, 145);
        graphics.DrawRectangle(new Pen(muted, Hairline), 2.5f, 2.5f, 10, 2.5f);
        graphics.DrawRectangle(new Pen(muted, Hairline), 3.5f, 6.5f, 10, 2.5f);
        graphics.DrawRectangle(new Pen(color, Emphasis), 4.5f, 10.5f, 9, 2.5f);
        return bitmap;
    }

    internal static Bitmap CartesianPlane()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var grid = FoundryTheme.WithAlpha(color, 110);
        graphics.FillEllipse(grid, 7, 4, 1.5f, 1.5f);
        graphics.FillEllipse(grid, 11, 4, 1.5f, 1.5f);
        graphics.FillEllipse(grid, 7, 8, 1.5f, 1.5f);
        graphics.FillEllipse(grid, 11, 8, 1.5f, 1.5f);
        var axisPen = new Pen(color, Emphasis);
        graphics.DrawLine(axisPen, 3, 12.5f, 14, 12.5f);
        graphics.DrawLine(axisPen, 3.5f, 13, 3.5f, 2);
        graphics.DrawLine(axisPen, 14, 12.5f, 11.5f, 10.5f);
        graphics.DrawLine(axisPen, 14, 12.5f, 11.5f, 14.5f);
        graphics.DrawLine(axisPen, 3.5f, 2, 1.5f, 4.5f);
        graphics.DrawLine(axisPen, 3.5f, 2, 5.5f, 4.5f);
        graphics.FillEllipse(color, 2.75f, 11.75f, 1.5f, 1.5f);
        return bitmap;
    }

    internal static Bitmap NewFolder()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 1.5f, 5, 1.5f, 13.5f);
        graphics.DrawLine(pen, 1.5f, 13.5f, 11.5f, 13.5f);
        graphics.DrawLine(pen, 11.5f, 13.5f, 11.5f, 6);
        graphics.DrawLine(pen, 1.5f, 5, 5, 5);
        graphics.DrawLine(pen, 5, 5, 6.5f, 6.5f);
        graphics.DrawLine(pen, 6.5f, 6.5f, 11.5f, 6.5f);
        DrawPlus(graphics, color, 12.5f, 3.5f);
        return bitmap;
    }

    internal static Bitmap NewLayout()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        graphics.DrawRectangle(new Pen(color, Emphasis), 1.5f, 3.5f, 9, 10.5f);
        graphics.DrawRectangle(new Pen(color, Hairline), 3.5f, 6, 5, 5.5f);
        DrawPlus(graphics, color, 12.5f, 3.5f);
        return bitmap;
    }

    internal static Bitmap Properties()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 2, 4, 14, 4);
        graphics.DrawLine(pen, 2, 8, 14, 8);
        graphics.DrawLine(pen, 2, 12, 14, 12);
        graphics.DrawEllipse(pen, 4.5f, 2, 4, 4);
        graphics.DrawEllipse(pen, 9.5f, 6, 4, 4);
        graphics.DrawEllipse(pen, 6.5f, 10, 4, 4);
        return bitmap;
    }

    internal static Bitmap Delete()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 3, 4.5f, 13, 4.5f);
        graphics.DrawLine(pen, 6, 2.5f, 10, 2.5f);
        graphics.DrawLine(pen, 6, 2.5f, 5.5f, 4.5f);
        graphics.DrawLine(pen, 10, 2.5f, 10.5f, 4.5f);
        graphics.DrawRectangle(pen, 4.5f, 5.5f, 7, 8);
        graphics.DrawLine(pen, 7, 7, 7, 12);
        graphics.DrawLine(pen, 9, 7, 9, 12);
        return bitmap;
    }

    internal static Bitmap ImportPackage() => TransferPackage(arrowPointsDown: true);

    internal static Bitmap ExportPackage() => TransferPackage(arrowPointsDown: false);

    internal static Bitmap FitAll()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawLine(pen, 2, 6, 2, 2);
        graphics.DrawLine(pen, 2, 2, 6, 2);
        graphics.DrawLine(pen, 10, 2, 14, 2);
        graphics.DrawLine(pen, 14, 2, 14, 6);
        graphics.DrawLine(pen, 14, 10, 14, 14);
        graphics.DrawLine(pen, 14, 14, 10, 14);
        graphics.DrawLine(pen, 6, 14, 2, 14);
        graphics.DrawLine(pen, 2, 14, 2, 10);
        return bitmap;
    }

    internal static Bitmap FocusSelection()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawEllipse(pen, 3, 3, 10, 10);
        graphics.DrawEllipse(pen, 6, 6, 4, 4);
        graphics.DrawLine(pen, 8, 1, 8, 4);
        graphics.DrawLine(pen, 8, 12, 8, 15);
        graphics.DrawLine(pen, 1, 8, 4, 8);
        graphics.DrawLine(pen, 12, 8, 15, 8);
        return bitmap;
    }

    internal static Bitmap Tidy()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawRectangle(pen, 2, 2, 5, 5);
        graphics.DrawRectangle(pen, 9, 2, 5, 5);
        graphics.DrawRectangle(pen, 2, 9, 5, 5);
        graphics.DrawRectangle(pen, 9, 9, 5, 5);
        return bitmap;
    }

    internal static Bitmap ZoomOut() => ZoomGlyph(includePlus: false);

    internal static Bitmap ZoomIn() => ZoomGlyph(includePlus: true);

    internal static Bitmap Navigator()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 4, 4, 13.5f, 4);
        graphics.DrawLine(pen, 4, 8, 13.5f, 8);
        graphics.DrawLine(pen, 4, 12, 13.5f, 12);
        graphics.FillEllipse(color, 1.25f, 3.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 1.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 1.25f, 11.25f, 1.5f, 1.5f);
        return bitmap;
    }

    internal static Bitmap NamedViews()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawRectangle(pen, 1.5f, 2.5f, 13, 11);
        graphics.FillEllipse(color, 10.75f, 4.75f, 1.5f, 1.5f);
        graphics.DrawLine(pen, 3, 11.5f, 6.25f, 7.5f);
        graphics.DrawLine(pen, 6.25f, 7.5f, 8.5f, 10);
        graphics.DrawLine(pen, 8.5f, 10, 10, 8.5f);
        graphics.DrawLine(pen, 10, 8.5f, 13, 11.5f);
        return bitmap;
    }

    internal static Bitmap OpenSelection()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawLine(pen, 3, 6, 3, 13);
        graphics.DrawLine(pen, 3, 13, 10, 13);
        graphics.DrawLine(pen, 7, 3, 13, 3);
        graphics.DrawLine(pen, 13, 3, 13, 9);
        graphics.DrawLine(pen, 7, 9, 13, 3);
        return bitmap;
    }

    internal static Bitmap More()
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var color = FoundryTheme.PrimaryText;
        graphics.FillEllipse(color, 2.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 7.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 12.25f, 7.25f, 1.5f, 1.5f);
        return bitmap;
    }

    private static Bitmap ZoomGlyph(bool includePlus)
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawEllipse(pen, 2, 2, 9, 9);
        graphics.DrawLine(pen, 9.5f, 9.5f, 14, 14);
        graphics.DrawLine(pen, 4.25f, 6.5f, 8.75f, 6.5f);
        if (includePlus)
            graphics.DrawLine(pen, 6.5f, 4.25f, 6.5f, 8.75f);
        return bitmap;
    }

    private static Bitmap TransferPackage(bool arrowPointsDown)
    {
        var bitmap = NewBitmap();
        using var graphics = new Graphics(bitmap) { AntiAlias = true };
        var pen = new Pen(FoundryTheme.PrimaryText, 1.15f);
        graphics.DrawRectangle(pen, 2, 8.5f, 12, 5.5f);
        graphics.DrawLine(pen, 5, 11, 11, 11);
        if (arrowPointsDown)
        {
            graphics.DrawLine(pen, 8, 1.5f, 8, 8);
            graphics.DrawLine(pen, 5.5f, 5.5f, 8, 8);
            graphics.DrawLine(pen, 10.5f, 5.5f, 8, 8);
        }
        else
        {
            graphics.DrawLine(pen, 8, 8, 8, 1.5f);
            graphics.DrawLine(pen, 5.5f, 4, 8, 1.5f);
            graphics.DrawLine(pen, 10.5f, 4, 8, 1.5f);
        }
        return bitmap;
    }

    private static void DrawPlus(Graphics graphics, Color color, float x, float y)
    {
        var pen = new Pen(color, Emphasis);
        graphics.DrawLine(pen, x - 2.5f, y, x + 2.5f, y);
        graphics.DrawLine(pen, x, y - 2.5f, x, y + 2.5f);
    }

    private static Bitmap NewBitmap() =>
        new(IconSize, IconSize, PixelFormat.Format32bppRgba);
}
