using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class RenameFolderDialog : Dialog
{
    private readonly TextBox _nameTextBox;
    private readonly FoundryDialogButton _renameButton;

    internal RenameFolderDialog(string currentName)
    {
        Title = "Rename folder";
        MinimumSize = new Size(360, 150);
        Resizable = false;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _nameTextBox = new TextBox { Text = currentName };
        _renameButton = new FoundryDialogButton(
            "Rename",
            FoundryDialogButtonStyle.Primary,
            80)
        {
            Enabled = false,
        };
        var cancelButton = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);

        _nameTextBox.TextChanged += (_, _) =>
            _renameButton.Enabled = !string.IsNullOrWhiteSpace(_nameTextBox.Text) &&
                                    !string.Equals(_nameTextBox.Text.Trim(), currentName, StringComparison.Ordinal);
        _nameTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Keys.Enter && _renameButton.Enabled)
            {
                Accept();
                eventArgs.Handled = true;
            }
        };
        _renameButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, _renameButton, cancelButton);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = "Rename folder",
                    Font = SystemFonts.Bold(15),
                    TextColor = FoundryTheme.PrimaryText,
                },
                new FoundryFormField(_nameTextBox),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space2, 0),
                    Rows =
                    {
                        new TableRow(
                            new TableCell(null, scaleWidth: true),
                            cancelButton,
                            _renameButton),
                    },
                },
            },
        };
    }

    internal bool Accepted { get; private set; }

    internal string FolderName => _nameTextBox.Text.Trim();

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        _nameTextBox.SelectAll();
        _nameTextBox.Focus();
    }

    private void Accept()
    {
        if (!_renameButton.Enabled)
        {
            return;
        }

        Accepted = true;
        Close();
    }
}
