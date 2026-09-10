using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class DetailCaptionsDialog : Dialog
{
    private readonly DropDown _name = new();
    private readonly DropDown _scale = new();
    public bool Accepted { get; private set; }
    public DetailCaptionSettings Settings => new((CaptionVisibility)_name.SelectedIndex, (CaptionVisibility)_scale.SelectedIndex);
    public DetailCaptionsDialog(DetailCaptionSettings settings, EffectiveDetailCaption inherited, bool parallel = true)
    {
        Title = "Detail captions";
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;
        MinimumSize = new Size(380, 230);
        foreach (var choice in new[] { "Inherit", "Show", "Hide" }) { _name.Items.Add(choice); _scale.Items.Add(choice); }
        _name.SelectedIndex = (int)settings.Name;
        _scale.SelectedIndex = (int)settings.Scale;
        var effective = new Label { TextColor = FoundryTheme.PrimaryText };
        void Update()
        {
            var value = Settings;
            var name = value.Name == CaptionVisibility.Inherit ? inherited.Name : value.Name == CaptionVisibility.Show;
            var scale = parallel && (value.Scale == CaptionVisibility.Inherit ? inherited.Scale : value.Scale == CaptionVisibility.Show);
            effective.Text = $"Effective: name {(name ? "shown" : "hidden")}, scale {(scale ? "shown for parallel views" : "hidden")}.";
        }
        _name.SelectedIndexChanged += (_, _) => Update();
        _scale.SelectedIndexChanged += (_, _) => Update();
        var save = new FoundryDialogButton("Apply", FoundryDialogButtonStyle.Primary);
        var cancel = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary);
        save.Click += (_, _) => { Accepted = true; Close(); };
        cancel.Click += (_, _) => Close();
        FoundryDialogActions.Bind(this, save, cancel);
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { new Label { Text = "Name", TextColor = FoundryTheme.PrimaryText }, new FoundryFormField(_name),
                new Label { Text = "Scale", TextColor = FoundryTheme.PrimaryText }, new FoundryFormField(_scale), effective,
                new Label { Text = "Applies to managed details only. Perspective views never show scale.", TextColor = FoundryTheme.PrimaryText },
                new TableLayout { Rows = { new TableRow(new TableCell(null, true), cancel, save) } } }
        };
        Update();
    }
}
