using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class CustomPaperSizeDialog : Dialog
{
    private readonly NumericStepper _width;
    private readonly NumericStepper _height;
    private readonly DropDown _units;
    private readonly Label _status;

    internal CustomPaperSizeDialog(double width, double height, string unitSystem)
    {
        Title = "Custom paper size";
        MinimumSize = new Size(360, 250);
        Resizable = false;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _width = DimensionStepper(width);
        _height = DimensionStepper(height);
        _units = new DropDown
        {
            DataStore = Units,
            SelectedIndex = Math.Max(0, Array.FindIndex(
                Units,
                unit => string.Equals(unit, unitSystem, StringComparison.OrdinalIgnoreCase))),
        };
        _status = FoundryTheme.MutedLabel();
        var cancel = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        var apply = new FoundryDialogButton(
            "Apply",
            FoundryDialogButtonStyle.Primary,
            84);
        cancel.Click += (_, _) => Close();
        apply.Click += (_, _) =>
        {
            if (_width.Value <= 0 || _height.Value <= 0)
            {
                _status.Text = "Width and height must both be greater than zero.";
                return;
            }

            Accepted = true;
            Close();
        };
        FoundryDialogActions.Bind(this, apply, cancel);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            Items =
            {
                new Label
                {
                    Text = "Custom paper size",
                    Font = SystemFonts.Bold(17),
                    TextColor = FoundryTheme.PrimaryText,
                },
                FoundryTheme.MutedLabel("Set the physical page dimensions for the selected layouts."),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space3, FoundryTheme.Space2),
                    Rows =
                    {
                        new TableRow(new Label { Text = "Width" }, new FoundryFormField(_width)),
                        new TableRow(new Label { Text = "Height" }, new FoundryFormField(_height)),
                        new TableRow(new Label { Text = "Units" }, new FoundryFormField(_units)),
                    },
                },
                _status,
                new TableLayout
                {
                    Rows = { new TableRow(new TableCell(null, true), cancel, apply) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
    }

    internal bool Accepted { get; private set; }

    internal double PaperWidth => _width.Value;

    internal double PaperHeight => _height.Value;

    internal string UnitSystem => _units.SelectedValue as string ?? "Millimeters";

    private static NumericStepper DimensionStepper(double value) => new()
    {
        Value = Math.Max(0.001, value),
        MinValue = 0.001,
        MaxValue = 100000,
        DecimalPlaces = 3,
        Increment = 1,
        Width = 140,
    };

    private static readonly string[] Units =
        ["Millimeters", "Centimeters", "Meters", "Inches", "Feet"];
}
