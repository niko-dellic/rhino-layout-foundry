using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Edits one reusable appearance-state resource. Target-local overrides are
/// deliberately edited elsewhere so the reusable basis remains unambiguous.
/// </summary>
internal sealed class AppearanceStateEditorDialog : Dialog
{
    private readonly DocumentSnapshot _snapshot;
    private readonly AppearanceStateRecord _state;
    private readonly TextBox _search = new() { PlaceholderText = "Search layers" };
    private readonly GridView _grid;
    private readonly Label _status = FoundryTheme.MutedLabel();
    private readonly FoundryDialogButton _save = new("Save", FoundryDialogButtonStyle.Secondary, 84);
    private LayerRow[] _layerRows = [];
    private readonly Dictionary<Guid, LayerVisibilityOverride> _layerValues;
    private readonly List<ObjectDisplayRule> _objectRules;
    private readonly Dictionary<string, Guid> _objectIds;
    private readonly Dictionary<string, Guid> _layerIds;
    private readonly Dictionary<string, Guid> _modeIds;
    private readonly FilteredPicker _objectPicker;
    private readonly FilteredPicker _layerPicker;
    private readonly FilteredPicker _modePicker;
    private readonly FoundryCheckBox _children = new("Include child layers");

    internal AppearanceStateEditorDialog(DocumentSnapshot snapshot, AppearanceStateRecord state)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _layerValues = state.LayerRules
            .GroupBy(rule => rule.Layer.LayerId)
            .ToDictionary(group => group.Key, group => group.Last().Visibility);
        _objectRules = state.ObjectDisplayRules.ToList();
        _objectIds = snapshot.ModelObjects.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(ObjectLabel, item => item.Id, StringComparer.OrdinalIgnoreCase);
        _layerIds = snapshot.LayerSnapshots.Values
            .OrderBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.FullPath, item => item.Id, StringComparer.OrdinalIgnoreCase);
        _modeIds = snapshot.DisplayModes
            .Where(item => item.Key != Guid.Empty && !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);
        _objectPicker = new FilteredPicker(_objectIds.Keys, "Search model objects");
        _layerPicker = new FilteredPicker(_layerIds.Keys, "Search layers");
        _modePicker = new FilteredPicker(_modeIds.Keys, "Search display modes");
        _modePicker.Text = _modeIds.Keys.FirstOrDefault() ?? string.Empty;

        Title = state.Kind == AppearanceStateKind.LayerState
            ? $"Layout Foundry — {state.Name}"
            : $"Layout Foundry — {state.Name}";
        MinimumSize = new Size(720, 500);
        Size = new Size(820, 640);
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _grid = new GridView
        {
            ShowHeader = true,
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            RowHeight = 27,
            GridLines = GridLines.None,
            BackgroundColor = FoundryTheme.CanvasOverlayBackground,
        };
        if (state.Kind == AppearanceStateKind.LayerState)
        {
            _grid.Columns.Add(new GridColumn
            {
                HeaderText = "Layer",
                DataCell = new TextBoxCell(nameof(LayerRow.Name)),
                AutoSize = true,
            });
            _grid.Columns.Add(new GridColumn
            {
                HeaderText = "State",
                DataCell = new TextBoxCell(nameof(LayerRow.State)),
                Width = 100,
            });
        }
        else
        {
            _grid.Columns.Add(new GridColumn
            {
                HeaderText = "Target",
                DataCell = new TextBoxCell(nameof(ObjectRow.Target)),
                AutoSize = true,
            });
            _grid.Columns.Add(new GridColumn
            {
                HeaderText = "Display mode",
                DataCell = new TextBoxCell(nameof(ObjectRow.Mode)),
                Width = 180,
            });
        }

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
                state.Kind == AppearanceStateKind.LayerState
                    ? CreateLayerEditor()
                    : CreateObjectEditor(),
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

    private Control CreateLayerEditor()
    {
        _search.TextChanged += (_, _) => ReloadLayers();
        var inherit = new FoundryDialogButton("Inherit", FoundryDialogButtonStyle.Secondary, 82);
        var on = new FoundryDialogButton("On", FoundryDialogButtonStyle.Secondary, 60);
        var off = new FoundryDialogButton("Off", FoundryDialogButtonStyle.Secondary, 60);
        inherit.Click += (_, _) => SetSelectedLayers(null);
        on.Click += (_, _) => SetSelectedLayers(LayerVisibilityOverride.Visible);
        off.Click += (_, _) => SetSelectedLayers(LayerVisibilityOverride.Hidden);
        ReloadLayers();
        return new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                FoundryTheme.MutedLabel("Reusable layer-state basis"),
                new FoundryFormField(_search),
                new StackLayoutItem(_grid, true),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items = { inherit, on, off },
                },
            },
        };
    }

    private Control CreateObjectEditor()
    {
        var addObject = new FoundryDialogButton("Add object", FoundryDialogButtonStyle.Secondary, 104);
        var addLayer = new FoundryDialogButton("Add layer", FoundryDialogButtonStyle.Secondary, 96);
        var remove = new FoundryDialogButton("Remove selected", FoundryDialogButtonStyle.Secondary, 126);
        addObject.Click += (_, _) => AddObjectRule();
        addLayer.Click += (_, _) => AddLayerRule();
        remove.Click += (_, _) => RemoveObjectRules();
        ReloadObjectRules();
        return new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                FoundryTheme.MutedLabel("Reusable custom object-display basis"),
                Field("Display mode", _modePicker),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items =
                    {
                        new StackLayoutItem(Field("Exact object", _objectPicker), true),
                        addObject,
                    },
                },
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items =
                    {
                        new StackLayoutItem(Field("Objects on layer", _layerPicker), true),
                        addLayer,
                    },
                },
                _children,
                new StackLayoutItem(_grid, true),
                remove,
            },
        };
    }

    private static Control Field(string label, Control control) => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items = { FoundryTheme.MutedLabel(label), control },
    };

    private void ReloadLayers()
    {
        var query = _search.Text.Trim();
        _layerRows = _snapshot.LayerSnapshots.Values
            .Where(layer => query.Length == 0 ||
                            layer.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(layer => layer.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(layer => new LayerRow(
                layer.Id,
                layer.FullPath,
                _layerValues.GetValueOrDefault(layer.Id) switch
                {
                    LayerVisibilityOverride.Visible => "On",
                    LayerVisibilityOverride.Hidden => "Off",
                    _ => "Inherit",
                }))
            .ToArray();
        _grid.DataStore = _layerRows;
    }

    private void SetSelectedLayers(LayerVisibilityOverride? value)
    {
        foreach (var index in _grid.SelectedRows.Where(index => index >= 0 && index < _layerRows.Length))
        {
            var row = _layerRows[index];
            if (value is { } next) _layerValues[row.Id] = next;
            else _layerValues.Remove(row.Id);
            row.State = value switch
            {
                LayerVisibilityOverride.Visible => "On",
                LayerVisibilityOverride.Hidden => "Off",
                _ => "Inherit",
            };
        }
        _grid.DataStore = _layerRows;
    }

    private bool TryMode(out Guid modeId, out string modeName)
    {
        modeName = _modePicker.Text.Trim();
        if (_modeIds.TryGetValue(modeName, out modeId)) return true;
        _status.Text = "Choose a display mode.";
        return false;
    }

    private void AddObjectRule()
    {
        if (!TryMode(out var modeId, out var modeName) ||
            !_objectIds.TryGetValue(_objectPicker.Text.Trim(), out var objectId))
        {
            _status.Text = "Choose a model object and display mode.";
            return;
        }
        _objectRules.RemoveAll(rule => rule.Selector.Kind == ObjectDisplaySelectorKind.ExactObject &&
                                       rule.Selector.ObjectId == objectId);
        _objectRules.Add(new ObjectDisplayRule(
            new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: objectId),
            modeId, modeName));
        ReloadObjectRules();
    }

    private void AddLayerRule()
    {
        if (!TryMode(out var modeId, out var modeName) ||
            !_layerIds.TryGetValue(_layerPicker.Text.Trim(), out var layerId))
        {
            _status.Text = "Choose a layer and display mode.";
            return;
        }
        var selector = new ObjectDisplaySelector(ObjectDisplaySelectorKind.Layer,
            LayerId: layerId, LayerFullPath: _layerPicker.Text.Trim(),
            IncludeChildLayers: _children.Checked == true);
        _objectRules.RemoveAll(rule => rule.Selector == selector);
        _objectRules.Add(new ObjectDisplayRule(selector, modeId, modeName));
        ReloadObjectRules();
    }

    private void RemoveObjectRules()
    {
        foreach (var index in _grid.SelectedRows.OrderByDescending(index => index))
            if (index >= 0 && index < _objectRules.Count) _objectRules.RemoveAt(index);
        ReloadObjectRules();
    }

    private void ReloadObjectRules() => _grid.DataStore = _objectRules.Select(rule => new ObjectRow(
        rule.Selector.Kind == ObjectDisplaySelectorKind.ExactObject
            ? _snapshot.ModelObjects.GetValueOrDefault(rule.Selector.ObjectId ?? Guid.Empty)?.Name ?? "Missing object"
            : $"Layer: {rule.Selector.LayerFullPath}{(rule.Selector.IncludeChildLayers ? " + children" : string.Empty)}",
        rule.DisplayModeName)).ToArray();

    private async Task SaveAsync()
    {
        _save.Enabled = false;
        var result = _state.Kind == AppearanceStateKind.LayerState
            ? await LayoutFoundryUiHost.UpdateAppearanceStateAsync(
                _state.Id,
                layerRules: _layerValues.Select(pair =>
                    new LayerVisibilityRule(
                        new LayerReference(pair.Key,
                            _snapshot.LayerSnapshots.GetValueOrDefault(pair.Key)?.FullPath ?? string.Empty),
                        pair.Value))
                    .ToArray())
            : await LayoutFoundryUiHost.UpdateAppearanceStateAsync(
                _state.Id,
                objectRules: _objectRules.ToArray());
        if (!result.Succeeded)
        {
            _save.Enabled = true;
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            return;
        }
        Changed = true;
        Close();
    }

    private static string ObjectLabel(ModelObjectSnapshot item) =>
        $"{(string.IsNullOrWhiteSpace(item.Name) ? "Unnamed object" : item.Name)} · {item.LayerFullPath} · {item.Id.ToString()[..8]}";

    private sealed class LayerRow(Guid id, string name, string state)
    {
        public Guid Id { get; } = id;
        public string Name { get; } = name;
        public string State { get; set; } = state;
    }

    private sealed record ObjectRow(string Target, string Mode);
}
