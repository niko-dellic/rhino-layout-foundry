using System.Drawing;

namespace RhinoLayoutFoundry.Rhino;

/// <summary>
/// Code-generated placeholder icon used until the final Foundry identity is
/// designed. Keeping it in code avoids a platform-specific bitmap asset.
/// </summary>
internal static class TemporaryPanelIcon
{
    private const int Size = 32;

    internal static Icon? Create()
    {
        try
        {
            using var stream = new MemoryStream(CreateIcoBytes());
            using var source = new Icon(stream);
            return (Icon)source.Clone();
        }
        catch
        {
            try
            {
                return (Icon)SystemIcons.Application.Clone();
            }
            catch
            {
                return null;
            }
        }
    }

    private static byte[] CreateIcoBytes()
    {
        const int bitmapHeaderSize = 40;
        var xorBytes = Size * Size * 4;
        var maskRowBytes = ((Size + 31) / 32) * 4;
        var maskBytes = maskRowBytes * Size;
        var imageBytes = bitmapHeaderSize + xorBytes + maskBytes;
        using var stream = new MemoryStream(22 + imageBytes);
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((byte)Size);
        writer.Write((byte)Size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(imageBytes);
        writer.Write(22);

        writer.Write(bitmapHeaderSize);
        writer.Write(Size);
        writer.Write(Size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(xorBytes);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        for (var y = Size - 1; y >= 0; y--)
        for (var x = 0; x < Size; x++)
        {
            var color = Pixel(x, y);
            writer.Write(color.B);
            writer.Write(color.G);
            writer.Write(color.R);
            writer.Write(color.A);
        }

        for (var y = Size - 1; y >= 0; y--)
        {
            for (var byteIndex = 0; byteIndex < maskRowBytes; byteIndex++)
            {
                byte transparentMask = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = byteIndex * 8 + bit;
                    if (x < Size && Pixel(x, y).A == 0)
                        transparentMask |= (byte)(1 << (7 - bit));
                }

                writer.Write(transparentMask);
            }
        }

        return stream.ToArray();
    }

    private static Color Pixel(int x, int y)
    {
        if (Inside(x, y, 4, 3, 21, 19))
            return Color.FromArgb(155, 57, 126, 136);
        if (Inside(x, y, 7, 6, 24, 23))
            return Color.FromArgb(220, 45, 150, 164);
        if (Inside(x, y, 10, 9, 28, 28))
        {
            var border = x is 10 or 28 || y is 9 or 28;
            if (border) return Color.FromArgb(255, 35, 177, 195);
            if (y is 21 or 22) return Color.FromArgb(255, 35, 177, 195);
            return Color.FromArgb(255, 239, 244, 244);
        }

        return Color.FromArgb(0, 0, 0, 0);
    }

    private static bool Inside(int x, int y, int left, int top, int right, int bottom) =>
        x >= left && x <= right && y >= top && y <= bottom;
}
