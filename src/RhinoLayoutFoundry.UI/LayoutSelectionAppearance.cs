using Eto.Drawing;

namespace RhinoLayoutFoundry.UI;

/// <summary>User interface preference, independent of documents and Rhino object colors.</summary>
internal static class LayoutSelectionAppearance
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RhinoLayoutFoundry", "selection-color.txt");
    private static Color? _customColor = Load();
    internal static Color Color => _customColor ?? LayoutPresentationTheme.DefaultSelectionAccent;
    private static bool _pendingSave;

    internal static void Set(Color color)
    {
        var opaque = new Color(color.R, color.G, color.B, 1f);
        if (Color == opaque) return;
        _customColor = opaque;
        _pendingSave = true;
    }

    internal static void Save()
    {
        if (!_pendingSave) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            var temporary = PreferencePath + ".tmp";
            File.WriteAllText(temporary, $"custom:{Color.Rb:X2}{Color.Gb:X2}{Color.Bb:X2}");
            File.Move(temporary, PreferencePath, overwrite: true);
            _pendingSave = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Keep the selected color for this session when preferences are read-only.
            _pendingSave = false;
        }
    }

    private static Color? Load()
    {
        try
        {
            var text = File.ReadAllText(PreferencePath).Trim();
            // The previous beta persisted its blue default while previewing the
            // picker. Migrate that default to the theme-aware neutral palette.
            if (text.Equals("2563EB", StringComparison.OrdinalIgnoreCase)) return null;
            if (text.StartsWith("custom:", StringComparison.Ordinal)) text = text[7..];
            if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var rgb))
                return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
