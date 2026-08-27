using Eto.Drawing;
using Rhino;

namespace RhinoLayoutFoundry.Rhino;

internal static class RhinoProjectIconLoader
{
    private const int IconSize = 16;

    internal static Image? Load()
    {
        try
        {
            return OperatingSystem.IsMacOS()
                ? LoadMacDocumentIcon()
                : OperatingSystem.IsWindows()
                    ? LoadWindowsApplicationIcon()
                    : null;
        }
        catch
        {
            // The text-only 3DM badge remains available when a Rhino installation
            // does not expose a platform icon in an expected location.
            return null;
        }
    }

    private static Image? LoadMacDocumentIcon()
    {
        var executableDirectory = RhinoApp.GetExecutableDirectory();
        if (executableDirectory is null) return null;

        var contentsDirectory = Directory.GetParent(executableDirectory.FullName)?.FullName;
        if (string.IsNullOrWhiteSpace(contentsDirectory)) return null;

        var iconPath = Path.Combine(contentsDirectory, "Resources", "rhinodoc.icns");
        return File.Exists(iconPath)
            ? new Icon(iconPath).WithSize(IconSize, IconSize)
            : null;
    }

    private static Image? LoadWindowsApplicationIcon()
    {
        var executableDirectory = RhinoApp.GetExecutableDirectory();
        if (executableDirectory is null) return null;

        var executablePath = Path.Combine(executableDirectory.FullName, "Rhino.exe");
        if (!File.Exists(executablePath)) return null;

        using var nativeIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
        if (nativeIcon is null) return null;

        using var stream = new MemoryStream();
        nativeIcon.Save(stream);
        stream.Position = 0;
        return new Icon(stream).WithSize(IconSize, IconSize);
    }
}
