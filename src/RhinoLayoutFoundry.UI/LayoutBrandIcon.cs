using Eto.Drawing;
namespace RhinoLayoutFoundry.UI;
internal static class LayoutBrandIcon
{
    private const int IconSize = 16;
    private const int BrandMarkSize = 20;
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
