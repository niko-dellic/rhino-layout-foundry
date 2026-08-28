using System.Drawing;
using Rhino.Runtime;
using Rhino.UI;

namespace RhinoLayoutFoundry.Rhino;

internal static class PanelIcon
{
    private const int Size = 32;
    private const string ResourceName =
        "RhinoLayoutFoundry.Rhino.Resources.LayoutFoundryMark.svg";

    internal static Icon? Create()
    {
        try
        {
            var assembly = typeof(PanelIcon).Assembly;
            using var svgStream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Missing icon resource '{ResourceName}'.");
            using var reader = new StreamReader(svgStream);
            var pixels = DrawingUtilities.PixelsFromSvg(
                reader.ReadToEnd(),
                Size,
                Size,
                premultiplyAlpha: false,
                Color.Empty);

            if (pixels.Length != Size * Size * 4)
                throw new InvalidDataException("The panel icon rendered to an unexpected pixel buffer size.");

            if (HostUtils.RunningInDarkMode)
                DrawingUtilities.DarkModeConvertPixels(ref pixels);

            using var iconStream = new MemoryStream(CreateIcoBytes(pixels));
            using var source = new Icon(iconStream);
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

    private static byte[] CreateIcoBytes(byte[] rgbaPixels)
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
            var offset = (y * Size + x) * 4;
            writer.Write(rgbaPixels[offset + 2]);
            writer.Write(rgbaPixels[offset + 1]);
            writer.Write(rgbaPixels[offset]);
            writer.Write(rgbaPixels[offset + 3]);
        }

        for (var y = Size - 1; y >= 0; y--)
        {
            for (var byteIndex = 0; byteIndex < maskRowBytes; byteIndex++)
            {
                byte transparentMask = 0;
                for (var bit = 0; bit < 8; bit++)
                {
                    var x = byteIndex * 8 + bit;
                    if (x < Size && rgbaPixels[(y * Size + x) * 4 + 3] == 0)
                        transparentMask |= (byte)(1 << (7 - bit));
                }

                writer.Write(transparentMask);
            }
        }

        return stream.ToArray();
    }
}
