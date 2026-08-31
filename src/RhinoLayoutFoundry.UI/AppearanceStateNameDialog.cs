using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class AppearanceStateNameDialog : Dialog
{
    private readonly TextBox _name = new();
    private readonly Label _error = FoundryTheme.MutedLabel();
    private readonly FoundryDialogButton _create;
    private readonly HashSet<string> _siblingNames;

    internal AppearanceStateNameDialog(
        AppearanceStateKind kind,
        string destinationName,
        IEnumerable<string> siblingNames)
    {
        var kindLabel = kind == AppearanceStateKind.LayerState
            ? "Layer State"
            : "Object Display State";
        _siblingNames = siblingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Title = $"New {kindLabel}";
        MinimumSize = new Size(400, 176);
        Resizable = false;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _name.Text = $"New {kindLabel}";
        _create = new FoundryDialogButton("Create", FoundryDialogButtonStyle.Secondary, 84);
        var cancel = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary, 84);
        _name.TextChanged += (_, _) => Validate();
        _name.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Enter || !_create.Enabled) return;
            Accept();
            eventArgs.Handled = true;
        };
        _create.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, _create, cancel);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                FoundryTheme.MutedLabel($"Create in {destinationName}"),
                new FoundryFormField(_name),
                _error,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { cancel, _create },
                },
            },
        };
        Validate();
    }

    internal bool Accepted { get; private set; }

    internal string StateName => _name.Text.Trim();

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        _name.SelectAll();
        _name.Focus();
    }

    private void Validate()
    {
        var value = StateName;
        _error.Text = value.Length == 0
            ? "Enter a name."
            : _siblingNames.Contains(value)
                ? "A state with this name already exists in this folder."
                : string.Empty;
        _error.Visible = _error.Text.Length > 0;
        _create.Enabled = _error.Text.Length == 0;
    }

    private void Accept()
    {
        if (!_create.Enabled) return;
        Accepted = true;
        Close();
    }
}
