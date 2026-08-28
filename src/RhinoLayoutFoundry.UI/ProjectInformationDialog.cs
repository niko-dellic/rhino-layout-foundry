using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class ProjectInformationDialog : Dialog
{
    private readonly ProjectInformationEditor _editor;
    private readonly Label _status = FoundryTheme.MutedLabel();
    private readonly Button _save;

    internal ProjectInformationDialog(ProjectInformation information)
    {
        Title = "Project information";
        MinimumSize = new Size(720, 700);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;
        _editor = new ProjectInformationEditor(information);
        _save = FoundryTheme.ConfigureButton(new Button { Text = "Save project info" }, 128);
        var cancel = FoundryTheme.ConfigureButton(new Button { Text = "Cancel" });
        cancel.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        _editor.Changed += (_, _) =>
        {
            _status.Text = _editor.ValidationError ?? string.Empty;
            _save.Enabled = _editor.ValidationError is null;
        };
        DefaultButton = _save;
        AbortButton = cancel;
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = "Project information",
                    Font = SystemFonts.Bold(17),
                    TextColor = FoundryTheme.PrimaryText,
                },
                new StackLayoutItem(new Scrollable
                {
                    Border = BorderType.None,
                    ExpandContentWidth = true,
                    Content = _editor,
                }, true),
                _status,
                new TableLayout
                {
                    Rows = { new TableRow(new TableCell(null, true), cancel, _save) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
    }

    private async Task SaveAsync()
    {
        if (_editor.ValidationError is { } error)
        {
            _status.Text = error;
            return;
        }
        _save.Enabled = false;
        _save.Text = "Saving…";
        var result = await LayoutFoundryUiHost.UpdateProjectInformationAsync(_editor.Value);
        if (result.Succeeded)
        {
            Close();
            return;
        }
        _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
        _save.Text = "Save project info";
        _save.Enabled = true;
    }
}
