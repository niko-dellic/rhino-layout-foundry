using Eto.Drawing;
using Eto.Forms;

namespace RhinoLayoutFoundry.UI;

internal sealed class HierarchyNotesDialog : Dialog
{
    private readonly TextArea _notes;

    internal HierarchyNotesDialog(string title, string notes, bool isMixed)
    {
        Title = $"Layout Foundry — {title}";
        MinimumSize = new Size(420, 240);
        Size = new Size(460, 280);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _notes = new TextArea
        {
            Text = notes,
            Wrap = true,
            Height = 120,
        };
        var save = new FoundryDialogButton("Save", FoundryDialogButtonStyle.Primary, 88);
        var cancel = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary, 88);
        save.Click += (_, _) =>
        {
            Accepted = true;
            Close();
        };
        cancel.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, save, cancel);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = title,
                    Font = SystemFonts.Bold(15),
                    TextColor = FoundryTheme.PrimaryText,
                },
                new Panel
                {
                    Visible = isMixed,
                    Content = FoundryTheme.MutedLabel("Mixed notes — saving replaces all selected notes."),
                },
                new StackLayoutItem(new FoundryFormField(_notes), true),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space2, 0),
                    Rows =
                    {
                        new TableRow(
                            new TableCell(null, true),
                            cancel,
                            save),
                    },
                },
            },
        };
    }

    internal bool Accepted { get; private set; }

    internal string Notes => _notes.Text ?? string.Empty;

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        _notes.Focus();
        if (!string.IsNullOrEmpty(_notes.Text))
            _notes.CaretIndex = _notes.Text.Length;
    }
}
