using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

internal sealed class AppearanceStateEditorDialog : Dialog
{
    private readonly AppearanceStateRecord _state;
    private readonly bool _isNew;
    private readonly TextBox _name = new();
    private readonly TextArea _notes = new() { Height = 64, Wrap = true };
    private readonly Label _status = FoundryTheme.MutedLabel();
    private readonly FoundryDialogButton _save = new("Save", FoundryDialogButtonStyle.Secondary, 84);
    private readonly FoundryDialogButton _notesToggle = new("Add notes", FoundryDialogButtonStyle.Secondary, 96);
    private readonly AppearanceRulesTable _rules;

    internal AppearanceStateEditorDialog(DocumentSnapshot snapshot, AppearanceStateRecord state)
        : this(snapshot, state, isNew: false)
    {
    }

    internal static AppearanceStateEditorDialog ShowWithViewportPicking(
        Control parent,
        DocumentSnapshot snapshot,
        AppearanceStateRecord state) => ShowWithViewportPicking(parent, snapshot, state, isNew: false);

    internal static AppearanceStateEditorDialog ShowWithViewportPicking(
        Control parent,
        DocumentSnapshot snapshot,
        Guid folderId,
        string suggestedName) => ShowWithViewportPicking(
            parent,
            snapshot,
            new AppearanceStateRecord(Guid.Empty, folderId, 0, suggestedName, [], []),
            isNew: true);

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
        _notes.Text = state.Notes;
        _rules = new AppearanceRulesTable(snapshot, state.LayerRules, state.ObjectDisplayRules);
        _rules.PickObjectsRequested += (_, _) =>
        {
            PickRequested = true;
            Close();
        };

        Title = isNew
            ? "Layout Foundry — New Appearance State"
            : $"Layout Foundry — {state.Name}";
        MinimumSize = new Size(680, 480);
        Size = new Size(760, 560);
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        var close = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary, 84);
        close.Click += (_, _) => Close();
        _save.Click += async (_, _) => await SaveAsync();
        FoundryDialogActions.Bind(this, _save, close);

        var notesField = Field("Notes", new FoundryFormField(_notes));
        notesField.Visible = false;
        _notesToggle.Click += (_, _) =>
        {
            notesField.Visible = !notesField.Visible;
            _notesToggle.Text = notesField.Visible ? "Hide notes" : "Add notes";
            if (notesField.Visible) _notes.Focus();
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Field("Name", new FoundryFormField(_name)),
                new StackLayoutItem(_rules, true),
                notesField,
                _status,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    Items = { _notesToggle, null, close, _save },
                },
            },
        };
    }

    internal bool Changed { get; private set; }

    internal bool PickRequested { get; private set; }

    internal string StateName => _name.Text.Trim();

    internal string Notes => _notes.Text.Trim();

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
                _state.FolderId, name, _rules.LayerRules, _rules.ObjectDisplayRules, Notes)
            : await LayoutFoundryUiHost.UpdateAppearanceStateAsync(
                _state.Id, name, _rules.LayerRules, _rules.ObjectDisplayRules, Notes);
        if (!result.Succeeded)
        {
            _save.Enabled = true;
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            return;
        }

        Changed = true;
        Close();
    }

    private static AppearanceStateEditorDialog ShowWithViewportPicking(
        Control parent,
        DocumentSnapshot snapshot,
        AppearanceStateRecord initialState,
        bool isNew)
    {
        var draft = initialState;
        IReadOnlyList<Guid> pickedObjectIds = [];
        var pickerStatus = string.Empty;
        while (true)
        {
            var dialog = new AppearanceStateEditorDialog(snapshot, draft, isNew);
            if (pickedObjectIds.Count > 0) dialog._rules.SelectObjects(pickedObjectIds);
            dialog._status.Text = pickerStatus;
            dialog.ShowModal(parent);
            if (!dialog.PickRequested) return dialog;

            draft = draft with
            {
                Name = dialog.StateName,
                Notes = dialog.Notes,
                LayerRules = dialog._rules.LayerRules,
                ObjectDisplayRules = dialog._rules.ObjectDisplayRules,
            };
            // Let Eto finish tearing down the modal window before Rhino starts
            // its native GetObject loop, then let Rhino finish that loop before
            // reopening the editor. This avoids nested macOS modal sessions.
            Application.Instance.RunIteration();
            var result = LayoutFoundryUiHost.PickModelObjects();
            Application.Instance.RunIteration();
            pickedObjectIds = result.Succeeded ? result.ObjectIds : [];
            pickerStatus = result.Message;
        }
    }
}

internal sealed class LocalAppearanceRulesDialog : Dialog
{
    private readonly AppearanceRulesTable _rules;
    private readonly Label _status = FoundryTheme.MutedLabel();

    internal LocalAppearanceRulesDialog(
        DocumentSnapshot snapshot,
        IReadOnlyList<LayerVisibilityRule> layerRules,
        IReadOnlyList<ObjectDisplayRule> objectRules)
    {
        Title = "Layout Foundry — Local Appearance Overrides";
        MinimumSize = new Size(680, 460);
        Size = new Size(760, 540);
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;
        _rules = new AppearanceRulesTable(snapshot, layerRules, objectRules);
        _rules.PickObjectsRequested += (_, _) =>
        {
            PickRequested = true;
            Close();
        };
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
                _status,
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
    internal bool PickRequested { get; private set; }
    internal IReadOnlyList<LayerVisibilityRule> LayerRules => _rules.LayerRules;
    internal IReadOnlyList<ObjectDisplayRule> ObjectDisplayRules => _rules.ObjectDisplayRules;

    internal static LocalAppearanceRulesDialog ShowWithViewportPicking(
        Control parent,
        DocumentSnapshot snapshot,
        IReadOnlyList<LayerVisibilityRule> layerRules,
        IReadOnlyList<ObjectDisplayRule> objectRules)
    {
        var draftLayers = layerRules;
        var draftObjects = objectRules;
        IReadOnlyList<Guid> pickedObjectIds = [];
        var pickerStatus = string.Empty;
        while (true)
        {
            var dialog = new LocalAppearanceRulesDialog(snapshot, draftLayers, draftObjects);
            if (pickedObjectIds.Count > 0) dialog._rules.SelectObjects(pickedObjectIds);
            dialog._status.Text = pickerStatus;
            dialog.ShowModal(parent);
            if (!dialog.PickRequested) return dialog;

            draftLayers = dialog.LayerRules;
            draftObjects = dialog.ObjectDisplayRules;
            Application.Instance.RunIteration();
            var result = LayoutFoundryUiHost.PickModelObjects();
            Application.Instance.RunIteration();
            pickedObjectIds = result.Succeeded ? result.ObjectIds : [];
            pickerStatus = result.Message;
        }
    }
}
