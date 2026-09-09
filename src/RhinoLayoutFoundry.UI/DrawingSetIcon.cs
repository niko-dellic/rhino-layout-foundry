using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

/// <summary>Drawing Set identity shared by its create action and saved conversation rows.</summary>
public static class DrawingSetIcon
{
    /// <summary>Creates a caller-owned icon with normal and high-DPI frames.</summary>
    public static Image Create()
    {
        var frames = new List<IconFrame>();
        foreach (var scale in new[] { 1f, 2f, 3f })
        {
            var bitmap = new Bitmap((int)(16 * scale), (int)(16 * scale), PixelFormat.Format32bppRgba);
            using var graphics = new Graphics(bitmap) { AntiAlias = true };
            graphics.ScaleTransform(scale);
            var color = FoundryTheme.PrimaryText;
            using var pen = new Pen(color, 0.9f);
            graphics.DrawLine(pen, 8, 1.5f, 8, 14.5f);
            graphics.DrawLine(pen, 1.5f, 8, 14.5f, 8);
            graphics.DrawLine(pen, 3.4f, 3.4f, 12.6f, 12.6f);
            graphics.DrawLine(pen, 12.6f, 3.4f, 3.4f, 12.6f);
            graphics.FillEllipse(color, 6.2f, 6.2f, 3.6f, 3.6f);
            frames.Add(new IconFrame(scale, bitmap));
        }
        return new Icon(frames.ToArray());
    }
}
