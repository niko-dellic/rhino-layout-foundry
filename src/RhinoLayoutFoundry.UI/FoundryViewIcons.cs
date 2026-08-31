using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryViewIcons
{
    private const int IconSize = 16;
    private const int BrandMarkSize = 20;
    private const float Hairline = 0.8f;
    private const float Emphasis = 0.9f;
    private static readonly float[] IconScales = [1f, 2f, 3f];

    internal static Icon BrandMark() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        graphics.ScaleTransform(BrandMarkSize / 512f);
        graphics.FillPolygon(color,
        [
            new PointF(205, 64), new PointF(307, 64),
            new PointF(340, 120), new PointF(172, 120),
        ]);
        graphics.FillPolygon(color,
        [
            new PointF(126, 120), new PointF(172, 120), new PointF(103, 240),
            new PointF(74, 290), new PointF(50, 248),
        ]);
        graphics.FillPolygon(color,
        [
            new PointF(340, 120), new PointF(386, 120), new PointF(430, 196),
            new PointF(384, 276), new PointF(332, 276), new PointF(378, 196),
        ]);
        graphics.FillPolygon(color,
        [
            new PointF(50, 292), new PointF(332, 292),
            new PointF(292, 360), new PointF(92, 360),
        ]);
        graphics.FillPolygon(color,
        [
            new PointF(177, 190), new PointF(287, 190),
            new PointF(258, 246), new PointF(206, 246),
        ]);
    }, BrandMarkSize);

    internal static Icon ListView() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.FillEllipse(color, 1.25f, 3.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 1.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 1.25f, 11.25f, 1.5f, 1.5f);
        graphics.DrawLine(pen, 4, 4, 14, 4);
        graphics.DrawLine(pen, 4, 8, 14, 8);
        graphics.DrawLine(pen, 4, 12, 14, 12);
    });

    internal static Icon Search() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Emphasis);
        graphics.DrawEllipse(pen, 2, 2, 8.5f, 8.5f);
        graphics.DrawLine(pen, 9.25f, 9.25f, 14, 14);
    });

    internal static Icon NamingPatternHelp() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Emphasis);
        graphics.DrawEllipse(pen, 1.5f, 1.5f, 13, 13);
        graphics.DrawArc(pen, new RectangleF(5, 3.5f, 6, 6), 205, 220);
        graphics.DrawLine(pen, 8, 8.5f, 8, 10.5f);
        graphics.FillEllipse(color, 7.25f, 12, 1.5f, 1.5f);
    });

    internal static Icon ThumbnailStack() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var muted = FoundryTheme.WithAlpha(color, 145);
        graphics.DrawRectangle(new Pen(muted, Hairline), 2.5f, 2.5f, 10, 2.5f);
        graphics.DrawRectangle(new Pen(muted, Hairline), 3.5f, 6.5f, 10, 2.5f);
        graphics.DrawRectangle(new Pen(color, Emphasis), 4.5f, 10.5f, 9, 2.5f);
    });

    internal static Icon CartesianPlane() => NewIcon(graphics =>
    {
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
    });

    internal static Icon NewFolder() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 1.5f, 5, 1.5f, 13.5f);
        graphics.DrawLine(pen, 1.5f, 13.5f, 11.5f, 13.5f);
        graphics.DrawLine(pen, 11.5f, 13.5f, 11.5f, 6);
        graphics.DrawLine(pen, 1.5f, 5, 5, 5);
        graphics.DrawLine(pen, 5, 5, 6.5f, 6.5f);
        graphics.DrawLine(pen, 6.5f, 6.5f, 11.5f, 6.5f);
        DrawPlus(graphics, color, 12.5f, 3.5f);
    });

    internal static Icon NewLayout() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        graphics.DrawRectangle(new Pen(color, Emphasis), 1.5f, 3.5f, 9, 10.5f);
        graphics.DrawRectangle(new Pen(color, Hairline), 3.5f, 6, 5, 5.5f);
        DrawPlus(graphics, color, 12.5f, 3.5f);
    });

    internal static Icon Properties() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 2, 4, 14, 4);
        graphics.DrawLine(pen, 2, 8, 14, 8);
        graphics.DrawLine(pen, 2, 12, 14, 12);
        graphics.DrawEllipse(pen, 4.5f, 2, 4, 4);
        graphics.DrawEllipse(pen, 9.5f, 6, 4, 4);
        graphics.DrawEllipse(pen, 6.5f, 10, 4, 4);
    });

    internal static Icon ProjectInformation() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var muted = FoundryTheme.WithAlpha(color, 155);
        var pen = new Pen(color, Emphasis);
        graphics.DrawRectangle(pen, 2, 1.5f, 12, 13);
        graphics.DrawEllipse(new Pen(muted, Hairline), 4, 4, 3, 3);
        graphics.DrawLine(new Pen(muted, Hairline), 3.75f, 9, 8, 9);
        graphics.DrawLine(new Pen(muted, Hairline), 3.75f, 11.5f, 8, 11.5f);
        graphics.DrawLine(pen, 9.5f, 5, 12.25f, 5);
        graphics.DrawLine(pen, 9.5f, 7.5f, 12.25f, 7.5f);
        graphics.DrawLine(pen, 9.5f, 10, 12.25f, 10);
    });

    internal static Icon Delete() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 3, 4.5f, 13, 4.5f);
        graphics.DrawLine(pen, 6, 2.5f, 10, 2.5f);
        graphics.DrawLine(pen, 6, 2.5f, 5.5f, 4.5f);
        graphics.DrawLine(pen, 10, 2.5f, 10.5f, 4.5f);
        graphics.DrawRectangle(pen, 4.5f, 5.5f, 7, 8);
        graphics.DrawLine(pen, 7, 7, 7, 12);
        graphics.DrawLine(pen, 9, 7, 9, 12);
    });

    internal static Icon ClearSelection() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 2, 5, 2, 2);
        graphics.DrawLine(pen, 2, 2, 5, 2);
        graphics.DrawLine(pen, 11, 2, 14, 2);
        graphics.DrawLine(pen, 14, 2, 14, 5);
        graphics.DrawLine(pen, 2, 11, 2, 14);
        graphics.DrawLine(pen, 2, 14, 5, 14);
        graphics.DrawLine(pen, 11, 14, 14, 14);
        graphics.DrawLine(pen, 14, 14, 14, 11);
        graphics.DrawLine(pen, 6, 6, 10, 10);
        graphics.DrawLine(pen, 10, 6, 6, 10);
    });

    internal static Icon ImportPackage() => TransferPackage(arrowPointsDown: true);

    internal static Icon ExportPackage() => TransferPackage(arrowPointsDown: false);

    internal static Icon FitAll() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        DrawCornerFrame(graphics, pen);
    });

    internal static Icon FocusSelection() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawEllipse(pen, 3, 3, 10, 10);
        graphics.DrawEllipse(pen, 6, 6, 4, 4);
        graphics.DrawLine(pen, 8, 1, 8, 4);
        graphics.DrawLine(pen, 8, 12, 8, 15);
        graphics.DrawLine(pen, 1, 8, 4, 8);
        graphics.DrawLine(pen, 12, 8, 15, 8);
    });

    internal static Icon Tidy() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawRectangle(pen, 2, 2, 5, 5);
        graphics.DrawRectangle(pen, 9, 2, 5, 5);
        graphics.DrawRectangle(pen, 2, 9, 5, 5);
        graphics.DrawRectangle(pen, 9, 9, 5, 5);
    });

    internal static Icon NestedPacking() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawRectangle(pen, 1.5f, 2, 13, 12.5f);
        graphics.DrawLine(pen, 1.5f, 5, 14.5f, 5);
        graphics.DrawRectangle(pen, 4, 7, 8, 5);
        graphics.DrawLine(pen, 4, 8.75f, 12, 8.75f);
    });

    internal static Icon CompactPacking() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawRectangle(pen, 1.5f, 2, 6, 5);
        graphics.DrawRectangle(pen, 8.5f, 2, 6, 5);
        graphics.DrawRectangle(pen, 1.5f, 8, 6, 5);
        graphics.DrawRectangle(pen, 8.5f, 8, 6, 5);
    });

    internal static Icon GridAppearance() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var muted = FoundryTheme.WithAlpha(color, 135);
        foreach (var x in new[] { 2.5f, 7f, 11.5f })
        foreach (var y in new[] { 2.5f, 7f, 11.5f })
            graphics.FillEllipse(muted, x, y, 1.5f, 1.5f);

        graphics.DrawEllipse(new Pen(color, Emphasis), 9.25f, 9.25f, 5.25f, 5.25f);
        graphics.FillEllipse(color, 11.1f, 11.1f, 1.6f, 1.6f);
    });

    internal static Icon PreviewBackground() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var muted = FoundryTheme.WithAlpha(color, 95);
        graphics.FillRectangle(muted, 2, 2, 12, 12);
        graphics.DrawRectangle(new Pen(color, Emphasis), 2, 2, 12, 12);
        graphics.DrawRectangle(new Pen(color, Hairline), 4.5f, 4.5f, 7, 7);
    });

    internal static Icon ZoomOut() => ZoomGlyph(includePlus: false);

    internal static Icon ZoomIn() => ZoomGlyph(includePlus: true);

    internal static Icon Navigator() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawLine(pen, 4, 4, 13.5f, 4);
        graphics.DrawLine(pen, 4, 8, 13.5f, 8);
        graphics.DrawLine(pen, 4, 12, 13.5f, 12);
        graphics.FillEllipse(color, 1.25f, 3.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 1.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 1.25f, 11.25f, 1.5f, 1.5f);
    });

    internal static Icon NamedViews() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        var pen = new Pen(color, Hairline);
        graphics.DrawRectangle(pen, 1.5f, 2.5f, 13, 11);
        graphics.FillEllipse(color, 10.75f, 4.75f, 1.5f, 1.5f);
        graphics.DrawLine(pen, 3, 11.5f, 6.25f, 7.5f);
        graphics.DrawLine(pen, 6.25f, 7.5f, 8.5f, 10);
        graphics.DrawLine(pen, 8.5f, 10, 10, 8.5f);
        graphics.DrawLine(pen, 10, 8.5f, 13, 11.5f);
    });

    internal static Icon OpenSelection() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawLine(pen, 3, 6, 3, 13);
        graphics.DrawLine(pen, 3, 13, 10, 13);
        graphics.DrawLine(pen, 7, 3, 13, 3);
        graphics.DrawLine(pen, 13, 3, 13, 9);
        graphics.DrawLine(pen, 7, 9, 13, 3);
    });

    internal static Icon Fullscreen() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        DrawCornerFrame(graphics, pen);
    });

    internal static Icon ExitFullscreen() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawLine(pen, 6, 1.5f, 6, 6);
        graphics.DrawLine(pen, 6, 6, 1.5f, 6);
        graphics.DrawLine(pen, 10, 1.5f, 10, 6);
        graphics.DrawLine(pen, 10, 6, 14.5f, 6);
        graphics.DrawLine(pen, 10, 14.5f, 10, 10);
        graphics.DrawLine(pen, 10, 10, 14.5f, 10);
        graphics.DrawLine(pen, 6, 14.5f, 6, 10);
        graphics.DrawLine(pen, 6, 10, 1.5f, 10);
    });

    internal static Icon Close() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Emphasis);
        graphics.DrawLine(pen, 4, 4, 12, 12);
        graphics.DrawLine(pen, 12, 4, 4, 12);
    });

    internal static Icon More() => NewIcon(graphics =>
    {
        var color = FoundryTheme.PrimaryText;
        graphics.FillEllipse(color, 2.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 7.25f, 7.25f, 1.5f, 1.5f);
        graphics.FillEllipse(color, 12.25f, 7.25f, 1.5f, 1.5f);
    });

    internal static Icon ChevronDown() => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Emphasis);
        graphics.DrawLine(pen, 4, 6, 8, 10);
        graphics.DrawLine(pen, 8, 10, 12, 6);
    });

    private static Icon ZoomGlyph(bool includePlus) => NewIcon(graphics =>
    {
        var pen = new Pen(FoundryTheme.PrimaryText, Hairline);
        graphics.DrawEllipse(pen, 2, 2, 9, 9);
        graphics.DrawLine(pen, 9.5f, 9.5f, 14, 14);
        graphics.DrawLine(pen, 4.25f, 6.5f, 8.75f, 6.5f);
        if (includePlus)
            graphics.DrawLine(pen, 6.5f, 4.25f, 6.5f, 8.75f);
    });

    private static Icon TransferPackage(bool arrowPointsDown) => NewIcon(graphics =>
    {
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
    });

    private static void DrawCornerFrame(Graphics graphics, Pen pen)
    {
        graphics.DrawLine(pen, 2, 6, 2, 2);
        graphics.DrawLine(pen, 2, 2, 6, 2);
        graphics.DrawLine(pen, 10, 2, 14, 2);
        graphics.DrawLine(pen, 14, 2, 14, 6);
        graphics.DrawLine(pen, 14, 10, 14, 14);
        graphics.DrawLine(pen, 14, 14, 10, 14);
        graphics.DrawLine(pen, 6, 14, 2, 14);
        graphics.DrawLine(pen, 2, 14, 2, 10);
    }

    private static void DrawPlus(Graphics graphics, Color color, float x, float y)
    {
        var pen = new Pen(color, Emphasis);
        graphics.DrawLine(pen, x - 2.5f, y, x + 2.5f, y);
        graphics.DrawLine(pen, x, y - 2.5f, x, y + 2.5f);
    }

    private static Icon NewIcon(Action<Graphics> draw, int size = IconSize)
    {
        var frames = new IconFrame[IconScales.Length];
        for (var index = 0; index < IconScales.Length; index++)
        {
            var scale = IconScales[index];
            var bitmap = new Bitmap(
                (int)(size * scale),
                (int)(size * scale),
                PixelFormat.Format32bppRgba);
            using var graphics = new Graphics(bitmap) { AntiAlias = true };
            graphics.ScaleTransform(scale);
            draw(graphics);
            frames[index] = new IconFrame(scale, bitmap);
        }

        return new Icon(frames);
    }
}
