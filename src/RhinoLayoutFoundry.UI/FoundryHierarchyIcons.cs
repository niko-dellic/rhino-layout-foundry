using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryHierarchyIcons
{
    private const int IconSize = 16;
    private static readonly float[] IconScales = [1f, 2f, 3f];
    private static readonly Icon RhinoIcon = NewIcon(DrawRhino);
    private static readonly Icon FolderIcon = NewIcon(DrawFolder);
    private static readonly Icon LayoutIcon = NewIcon(DrawLayout);
    private static readonly Icon DetailIcon = NewIcon(DrawDetail);
    private static readonly Icon ObjectIcon = NewIcon(DrawObject);
    private static readonly Icon AppearanceStateIcon = NewIcon(DrawAppearanceState);

    internal static Image Rhino => RhinoIcon;

    internal static Image Folder => FolderIcon;

    internal static Image Layout => LayoutIcon;

    internal static Image Detail => DetailIcon;

    internal static Image Object => ObjectIcon;

    internal static Image AppearanceState => AppearanceStateIcon;

    internal static void DrawRhino(Graphics graphics, Color color, RectangleF bounds)
    {
        using var pen = IconPen(color, bounds);
        // A compact geometric profile: shoulder, ear, brow, horn, muzzle and jaw.
        DrawLine(graphics, pen, bounds, 2, 9.75f, 2.75f, 6.25f);
        DrawLine(graphics, pen, bounds, 2.75f, 6.25f, 5.5f, 4.5f);
        DrawLine(graphics, pen, bounds, 5.5f, 4.5f, 8.5f, 4.5f);
        DrawLine(graphics, pen, bounds, 8.5f, 4.5f, 9.5f, 2.25f);
        DrawLine(graphics, pen, bounds, 9.5f, 2.25f, 10.25f, 5.25f);
        DrawLine(graphics, pen, bounds, 10.25f, 5.25f, 12.25f, 5.75f);
        DrawLine(graphics, pen, bounds, 12.25f, 5.75f, 14.5f, 3.75f);
        DrawLine(graphics, pen, bounds, 14.5f, 3.75f, 13.5f, 7.25f);
        DrawLine(graphics, pen, bounds, 13.5f, 7.25f, 14.75f, 8.25f);
        DrawLine(graphics, pen, bounds, 14.75f, 8.25f, 13, 10.25f);
        DrawLine(graphics, pen, bounds, 13, 10.25f, 10.75f, 10.5f);
        DrawLine(graphics, pen, bounds, 10.75f, 10.5f, 9.25f, 13.5f);
        DrawLine(graphics, pen, bounds, 9.25f, 13.5f, 5.25f, 13.5f);
        DrawLine(graphics, pen, bounds, 5.25f, 13.5f, 4.75f, 10.75f);
        DrawLine(graphics, pen, bounds, 4.75f, 10.75f, 2, 9.75f);
        FillEllipse(graphics, color, bounds, 10.65f, 6.5f, 1.15f, 1.15f);
        FillEllipse(graphics, color, bounds, 12.85f, 8.35f, 0.9f, 0.9f);
    }

    internal static void DrawFolder(Graphics graphics, Color color, RectangleF bounds)
    {
        using var pen = IconPen(color, bounds);
        DrawLine(graphics, pen, bounds, 2, 4.5f, 5.25f, 4.5f);
        DrawLine(graphics, pen, bounds, 5.25f, 4.5f, 6.75f, 6.25f);
        DrawLine(graphics, pen, bounds, 6.75f, 6.25f, 14, 6.25f);
        DrawLine(graphics, pen, bounds, 14, 6.25f, 14, 13.5f);
        DrawLine(graphics, pen, bounds, 14, 13.5f, 2, 13.5f);
        DrawLine(graphics, pen, bounds, 2, 13.5f, 2, 4.5f);
    }

    internal static void DrawLayout(Graphics graphics, Color color, RectangleF bounds)
    {
        using var pen = IconPen(color, bounds);
        DrawLine(graphics, pen, bounds, 3, 1.75f, 10.25f, 1.75f);
        DrawLine(graphics, pen, bounds, 10.25f, 1.75f, 13, 4.5f);
        DrawLine(graphics, pen, bounds, 13, 4.5f, 13, 14.25f);
        DrawLine(graphics, pen, bounds, 13, 14.25f, 3, 14.25f);
        DrawLine(graphics, pen, bounds, 3, 14.25f, 3, 1.75f);
        DrawLine(graphics, pen, bounds, 10.25f, 1.75f, 10.25f, 4.5f);
        DrawLine(graphics, pen, bounds, 10.25f, 4.5f, 13, 4.5f);
        DrawRectangle(graphics, pen, bounds, 5.25f, 7, 5.5f, 4.25f);
    }

    internal static void DrawDetail(Graphics graphics, Color color, RectangleF bounds)
    {
        using var pen = IconPen(color, bounds);
        DrawRectangle(graphics, pen, bounds, 4, 4, 8, 8);
        DrawLine(graphics, pen, bounds, 8, 1.75f, 8, 5.25f);
        DrawLine(graphics, pen, bounds, 8, 10.75f, 8, 14.25f);
        DrawLine(graphics, pen, bounds, 1.75f, 8, 5.25f, 8);
        DrawLine(graphics, pen, bounds, 10.75f, 8, 14.25f, 8);
    }

    internal static void DrawAppearanceState(Graphics graphics, Color color, RectangleF bounds)
    {
        using var pen = IconPen(color, bounds);
        DrawLine(graphics, pen, bounds, 2, 4, 8, 1.75f);
        DrawLine(graphics, pen, bounds, 8, 1.75f, 14, 4);
        DrawLine(graphics, pen, bounds, 14, 4, 8, 6.25f);
        DrawLine(graphics, pen, bounds, 8, 6.25f, 2, 4);
        DrawLine(graphics, pen, bounds, 2, 7.75f, 8, 10);
        DrawLine(graphics, pen, bounds, 8, 10, 14, 7.75f);
        DrawLine(graphics, pen, bounds, 2, 11.5f, 8, 13.75f);
        DrawLine(graphics, pen, bounds, 8, 13.75f, 14, 11.5f);
        DrawEllipse(graphics, pen, bounds, 10.25f, 9.75f, 4, 4);
    }

    internal static void DrawObject(Graphics graphics, Color color, RectangleF bounds)
    {
        using var pen = IconPen(color, bounds);
        DrawEllipse(graphics, pen, bounds, 5, 1.75f, 6, 2.75f);
        DrawLine(graphics, pen, bounds, 5, 3.1f, 2.5f, 12.75f);
        DrawLine(graphics, pen, bounds, 11, 3.1f, 13.5f, 12.75f);
        DrawEllipse(graphics, pen, bounds, 2.5f, 11.25f, 11, 3);
    }

    private static Icon NewIcon(Action<Graphics, Color, RectangleF> draw)
    {
        var frames = new IconFrame[IconScales.Length];
        for (var index = 0; index < IconScales.Length; index++)
        {
            var scale = IconScales[index];
            var bitmap = new Bitmap(
                (int)(IconSize * scale),
                (int)(IconSize * scale),
                PixelFormat.Format32bppRgba);
            using var graphics = new Graphics(bitmap) { AntiAlias = true };
            graphics.ScaleTransform(scale);
            draw(graphics, FoundryTheme.PrimaryText, new RectangleF(0, 0, IconSize, IconSize));
            frames[index] = new IconFrame(scale, bitmap);
        }

        return new Icon(frames);
    }

    private static Pen IconPen(Color color, RectangleF bounds)
    {
        var scale = Math.Min(bounds.Width, bounds.Height) / IconSize;
        return new Pen(color, Math.Max(0.8f, 0.9f * scale));
    }

    private static void DrawLine(
        Graphics graphics,
        Pen pen,
        RectangleF bounds,
        float x1,
        float y1,
        float x2,
        float y2) => graphics.DrawLine(
            pen,
            ScaleX(bounds, x1),
            ScaleY(bounds, y1),
            ScaleX(bounds, x2),
            ScaleY(bounds, y2));

    private static void DrawRectangle(
        Graphics graphics,
        Pen pen,
        RectangleF bounds,
        float x,
        float y,
        float width,
        float height) => graphics.DrawRectangle(
            pen,
            ScaleX(bounds, x),
            ScaleY(bounds, y),
            bounds.Width * width / IconSize,
            bounds.Height * height / IconSize);

    private static void DrawEllipse(
        Graphics graphics,
        Pen pen,
        RectangleF bounds,
        float x,
        float y,
        float width,
        float height) => graphics.DrawEllipse(
            pen,
            ScaleX(bounds, x),
            ScaleY(bounds, y),
            bounds.Width * width / IconSize,
            bounds.Height * height / IconSize);

    private static void FillEllipse(
        Graphics graphics,
        Color color,
        RectangleF bounds,
        float x,
        float y,
        float width,
        float height) => graphics.FillEllipse(
            color,
            ScaleX(bounds, x),
            ScaleY(bounds, y),
            bounds.Width * width / IconSize,
            bounds.Height * height / IconSize);

    private static float ScaleX(RectangleF bounds, float value) =>
        bounds.X + bounds.Width * value / IconSize;

    private static float ScaleY(RectangleF bounds, float value) =>
        bounds.Y + bounds.Height * value / IconSize;
}
