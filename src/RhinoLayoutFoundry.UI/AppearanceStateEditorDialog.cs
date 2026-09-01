using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class AppearanceStateEditorDialog : Dialog
{
    private readonly AppearanceStateRecord _state;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly Label _status = FoundryTheme.MutedLabel();
    private readonly FoundryDialogButton _save = new("Save", FoundryDialogButtonStyle.Secondary, 84);
    private readonly AppearanceRulesTable _rules;

    internal AppearanceStateEditorDialog(DocumentSnapshot snapshot, AppearanceStateRecord state)
        : this(snapshot, state, isNew: false)
    {
    }

    internal AppearanceStateEditorDialog(
        DocumentSnapshot snapshot,
        Guid folderId,
        string suggestedName)
        : this(snapshot, new AppearanceStateRecord(
            Guid.Empty, folderId, 0, suggestedName, [], []), isNew: true)
    {
    }

    private AppearanceStateEditorDialog(
        DocumentSnapshot snapshot,
        AppearanceStateRecord state,
        bool isNew)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _isNew = isNew;
        _name.Text = state.Name;
        _rules = new AppearanceRulesTable(snapshot, state.LayerRules, state.ObjectDisplayRules);

        Title = isNew
            ? "Layout Foundry — New Appearance State"
            : $"Layout Foundry — {state.Name}";
        MinimumSize = new Size(820, 560);
        Size = new Size(980, 720);
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        var close = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary, 84);
        close.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        FoundryDialogActions.Bind(this, _save, close);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Field("Name", new FoundryFormField(_name)),
                new StackLayoutItem(_rules, true),
                _status,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { close, _save },
                },
            },
        };
    }

    internal bool Changed { get; private set; }

    internal string StateName => _name.Text.Trim();

    protected override void OnShown(EventArgs eventArgs)
    {
        base.OnShown(eventArgs);
        if (!_isNew) return;
        _name.SelectAll();
        _name.Focus();
    }

    private static Control Field(string label, Control control) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items = { FoundryTheme.MutedLabel(label), control },
    };

    private async Task SaveAsync()
    {
        var name = StateName;
        if (name.Length == 0)
        {
            _status.Text = "Enter a name.";
            _name.Focus();
            return;
        }

        _save.Enabled = false;
        var result = _isNew
            ? await LayoutFoundryUiHost.CreateAppearanceStateAsync(
                _state.FolderId, name, _rules.LayerRules, _rules.ObjectDisplayRules)
            : await LayoutFoundryUiHost.UpdateAppearanceStateAsync(
                _state.Id, name, _rules.LayerRules, _rules.ObjectDisplayRules);
        if (!result.Succeeded)
        {
            _save.Enabled = true;
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            return;
        }

        Changed = true;
        Close();
    }
}

internal sealed class LocalAppearanceRulesDialog : Dialog
{
    private readonly AppearanceRulesTable _rules;

    internal LocalAppearanceRulesDialog(
        DocumentSnapshot snapshot,
        IReadOnlyList<LayerVisibilityRule> layerRules,
        IReadOnlyList<ObjectDisplayRule> objectRules)
    {
        Title = "Layout Foundry — Local Appearance Overrides";
        MinimumSize = new Size(820, 540);
        Size = new Size(980, 700);
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;
        _rules = new AppearanceRulesTable(snapshot, layerRules, objectRules);
        var cancel = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary, 84);
        var save = new FoundryDialogButton("Save", FoundryDialogButtonStyle.Secondary, 84);
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) =>
        {
            Accepted = true;
            Close();
        };
        FoundryDialogActions.Bind(this, save, cancel);
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_rules, true),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { cancel, save },
                },
            },
        };
    }

    internal bool Accepted { get; private set; }
    internal IReadOnlyList<LayerVisibilityRule> LayerRules => _rules.LayerRules;
    internal IReadOnlyList<ObjectDisplayRule> ObjectDisplayRules => _rules.ObjectDisplayRules;
}
