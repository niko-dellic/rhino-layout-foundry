using Eto.Drawing;
using Eto.Forms;
using Rhino.ApplicationSettings;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryTheme
{
    internal const int Space1 = 4;
    internal const int Space2 = 8;
    internal const int Space3 = 12;
    internal const int Space4 = 16;
    internal const int Space6 = 24;

    internal static Color PanelBackground => SystemColors.ControlBackground;

    internal static Color ContentBackground => PanelBackground;

    internal static Color PrimaryText => SystemColors.ControlText;

    internal static Color MutedText => SystemColors.DisabledText;

    internal static bool IsDarkMode
    {
        get
        {
            var background = SystemColors.ControlBackground;
            return background.Rb * 299 + background.Gb * 587 + background.Bb * 114 < 128000;
        }
    }

    // The infinite board is part of the same workspace, so its base follows
    // Rhino's panel background. Grid and card tokens provide the spatial depth.
    internal static Color CanvasBackground => PanelBackground;

    internal static Color CanvasSurface => IsDarkMode
        ? Color.FromArgb(39, 39, 42, 255)
        : Color.FromArgb(255, 255, 255, 255);

    internal static Color CanvasSubtleSurface => IsDarkMode
        ? Color.FromArgb(63, 63, 70, 255)
        : Color.FromArgb(244, 244, 245, 255);

    internal static Color CanvasBorder => IsDarkMode
        ? Color.FromArgb(82, 82, 91, 255)
        : Color.FromArgb(212, 212, 216, 255);

    internal static Color CanvasGrid => IsDarkMode
        ? Color.FromArgb(161, 161, 170, 30)
        : Color.FromArgb(113, 113, 122, 26);

    internal static Color SelectionAccent => RhinoColor(
        () => AppearanceSettings.SelectedObjectColor,
        Color.FromArgb(59, 130, 246, 255));

    internal static Color SelectionWindowStroke(bool crossing) => RhinoColor(
        () => crossing
            ? AppearanceSettings.SelectionWindowCrossingStrokeColor
            : AppearanceSettings.SelectionWindowStrokeColor,
        SelectionAccent);

    internal static Color SelectionWindowFill(bool crossing) => RhinoColor(
        () => crossing
            ? AppearanceSettings.SelectionWindowCrossingFillColor
            : AppearanceSettings.SelectionWindowFillColor,
        WithAlpha(SelectionAccent, 42));

    internal static Color WithAlpha(Color color, int alpha) =>
        Color.FromArgb(color.Rb, color.Gb, color.Bb, Math.Clamp(alpha, 0, 255));

    internal static Font BrandFont => SystemFonts.Bold(9);

    internal static Font EmptyTitleFont => SystemFonts.Bold(13);

    internal static Button ConfigureButton(Button button, int minimumWidth = 0)
    {
        button.MinimumSize = new Size(minimumWidth, 28);
        return button;
    }

    internal static Button ConfigureIconButton(Button button)
    {
        button.MinimumSize = new Size(28, 28);
        button.Width = 28;
        return button;
    }

    internal static Button ConfigureToolbarButton(Button button)
    {
        button.MinimumSize = new Size(24, 24);
        button.Width = 24;
        return button;
    }

    internal static Label MutedLabel(string text = "")
    {
        return new Label
        {
            Text = text,
            TextColor = MutedText,
            TextAlignment = TextAlignment.Left,
        };
    }

    internal static Panel Surface(Control content, Padding? padding = null)
    {
        return new Panel
        {
            BackgroundColor = ContentBackground,
            Padding = padding ?? new Padding(0),
            Content = content,
        };
    }

    private static Color RhinoColor(Func<System.Drawing.Color> getColor, Color fallback)
    {
        try
        {
            var color = getColor();
            return Color.FromArgb(color.R, color.G, color.B, color.A);
        }
        catch
        {
            return fallback;
        }
    }
}
