using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal static class FoundryTheme
{
    internal const int Space1 = 4;
    internal const int Space2 = 8;
    internal const int Space3 = 12;
    internal const int Space4 = 16;
    internal const int Space6 = 24;

    internal static Color PanelBackground => SystemColors.ControlBackground;

    internal static Color ContentBackground => SystemColors.WindowBackground;

    internal static Color PrimaryText => SystemColors.ControlText;

    internal static Color MutedText => SystemColors.DisabledText;

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
}
