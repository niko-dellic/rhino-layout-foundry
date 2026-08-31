using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class CanvasPreviewBackgroundTray : Panel
{
    private readonly Action<Color> _backgroundChanged;
    private readonly FoundryColorField _colorPicker;

    internal CanvasPreviewBackgroundTray(Color color, Action<Color> backgroundChanged)
    {
        _backgroundChanged = backgroundChanged ?? throw new ArgumentNullException(nameof(backgroundChanged));
        Size = new Size(280, 104);
        Padding = new Padding(1);
        BackgroundColor = FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 190);

        _colorPicker = new FoundryColorField(Opaque(color));
        _colorPicker.ValueChanged += (_, _) => PublishBackground();

        var reset = new FoundryDialogButton(
            "Reset",
            FoundryDialogButtonStyle.Secondary,
            58)
        {
            ToolTip = "Restore the white preview background",
        };
        reset.Click += (_, _) =>
        {
            _colorPicker.Value = FoundryTheme.CanvasPreviewBackground;
            PublishBackground();
        };

        Content = new Panel
        {
            Padding = new Padding(FoundryTheme.Space3),
            BackgroundColor = FoundryTheme.CanvasSurface,
            Content = new StackLayout
            {
                Spacing = FoundryTheme.Space2,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new TableLayout
                    {
                        Rows =
                        {
                            new TableRow(
                                new Label
                                {
                                    Text = "Preview background",
                                    Font = SystemFonts.Bold(10),
                                    TextColor = FoundryTheme.PrimaryText,
                                },
                                new TableCell(null, true),
                                reset),
                        },
                    },
                    new TableLayout
                    {
                        Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2),
                        Rows =
                        {
                            new TableRow(new Label { Text = "Color" }, _colorPicker),
                        },
                    },
                },
            },
        };
    }

    private void PublishBackground() => _backgroundChanged(Opaque(_colorPicker.Value));

    private static Color Opaque(Color color) => Color.FromArgb(color.Rb, color.Gb, color.Bb, 255);
}
