using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class CanvasGridTray : Panel
{
    private readonly Action<Color, double> _appearanceChanged;
    private readonly ColorPicker _colorPicker;
    private readonly Slider _opacitySlider;
    private readonly Label _opacityLabel;

    internal CanvasGridTray(
        Color color,
        double opacity,
        Action<Color, double> appearanceChanged)
    {
        _appearanceChanged = appearanceChanged ??
                             throw new ArgumentNullException(nameof(appearanceChanged));
        Size = new Size(300, 126);
        Padding = new Padding(1);
        BackgroundColor = FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 190);

        _colorPicker = new ColorPicker
        {
            Value = Opaque(color),
            AllowAlpha = false,
            Width = 170,
        };
        _opacitySlider = new Slider
        {
            MinValue = 0,
            MaxValue = 100,
            Value = (int)Math.Round(Math.Clamp(opacity, 0, 1) * 100),
            TickFrequency = 5,
            Width = 170,
        };
        _opacityLabel = FoundryTheme.MutedLabel();
        _opacityLabel.Width = 38;
        _opacityLabel.TextAlignment = TextAlignment.Right;
        UpdateOpacityLabel();

        _colorPicker.ValueChanged += (_, _) => PublishAppearance();
        _opacitySlider.ValueChanged += (_, _) =>
        {
            UpdateOpacityLabel();
            PublishAppearance();
        };

        var reset = new Button
        {
            Text = "Reset",
            ToolTip = "Restore the default grid color and opacity",
            MinimumSize = new Size(50, 24),
        };
        reset.Width = 50;
        reset.Click += (_, _) =>
        {
            _colorPicker.Value = FoundryTheme.CanvasGridColor;
            _opacitySlider.Value = (int)Math.Round(FoundryTheme.DefaultCanvasGridOpacity * 100);
            PublishAppearance();
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
                                    Text = "Canvas grid",
                                    Font = SystemFonts.Bold(10),
                                    TextColor = FoundryTheme.PrimaryText,
                                    TextAlignment = TextAlignment.Left,
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
                            new TableRow(new Label { Text = "Color" }, _colorPicker, null),
                            new TableRow(new Label { Text = "Opacity" }, _opacitySlider, _opacityLabel),
                        },
                    },
                },
            },
        };
    }

    private void PublishAppearance() =>
        _appearanceChanged(Opaque(_colorPicker.Value), _opacitySlider.Value / 100d);

    private void UpdateOpacityLabel() => _opacityLabel.Text = $"{_opacitySlider.Value}%";

    private static Color Opaque(Color color) => Color.FromArgb(color.Rb, color.Gb, color.Bb, 255);
}
