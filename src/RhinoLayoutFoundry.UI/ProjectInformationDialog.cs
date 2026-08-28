using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class ProjectInformationDialog : Dialog
{
    private readonly ProjectInformationEditor _editor;
    private readonly Label _status = FoundryTheme.MutedLabel();
    private readonly FoundryDialogButton _save;

    internal ProjectInformationDialog(ProjectInformation information)
    {
        Title = "Project information";
        MinimumSize = new Size(760, 600);
        Size = new Size(900, 760);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4 + FoundryTheme.Space1);
        BackgroundColor = FoundryTheme.PanelBackground;
        _editor = new ProjectInformationEditor(information);
        _save = new FoundryDialogButton(
            "Save project info",
            FoundryDialogButtonStyle.Primary,
            132);
        var cancel = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        cancel.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        _editor.Changed += (_, _) =>
        {
            _status.Text = _editor.ValidationError ?? string.Empty;
            _save.Enabled = _editor.ValidationError is null;
        };
        FoundryDialogActions.Bind(this, _save, cancel);
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space3,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items =
                    {
                        new ImageView
                        {
                            Image = FoundryViewIcons.ProjectInformation(),
                            Size = new Size(20, 20),
                        },
                        new Label
                        {
                            Text = "Project information",
                            Font = SystemFonts.Bold(17),
                            TextColor = FoundryTheme.PrimaryText,
                            TextAlignment = TextAlignment.Left,
                        },
                    },
                },
                new StackLayoutItem(new Scrollable
                {
                    Border = BorderType.None,
                    ExpandContentWidth = true,
                    Content = _editor,
                }, true),
                _status,
                new Panel
                {
                    Height = 1,
                    BackgroundColor = FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 145),
                },
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space2,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items =
                    {
                        new StackLayoutItem(null, expand: true),
                        cancel,
                        _save,
                    },
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
