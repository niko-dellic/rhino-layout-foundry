using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;

namespace RhinoLayoutFoundry.UI;

/// <summary>
/// Shared expandable editor for reusable appearance states and target-local rules.
/// Missing values are inheritance; only authored values are emitted on save.
/// </summary>
internal sealed class AppearanceRulesTable : Panel
{
    private const string Inherit = "Inherit";
    private readonly DocumentSnapshot _snapshot;
    private readonly TextBox _search = new() { PlaceholderText = "Search layers and objects" };
    private readonly TreeGridView _tree;
    private readonly GridColumn _visibilityColumn;
    private readonly GridColumn _displayModeColumn;
    private readonly Dictionary<Guid, LayerVisibilityOverride> _visibility;
    private readonly Dictionary<RuleTarget, ObjectDisplayRule> _displayRules;
    private readonly Dictionary<string, Guid> _displayModes;
    private readonly FilteredPicker _bulkDisplayMode;
    private AppearanceRow[] _roots = [];
    private RuleTarget? _editingTarget;
    private FilteredPicker? _activePicker;
    private RuleTarget? _armedPropertyTarget;
    private RuleTarget[] _propertyTargets = [];
    private RuleTarget[] _editingTargets = [];

    internal AppearanceRulesTable(
        DocumentSnapshot snapshot,
        IReadOnlyList<LayerVisibilityRule> layerRules,
        IReadOnlyList<ObjectDisplayRule> objectRules)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _visibility = layerRules
            .GroupBy(rule => rule.Layer.LayerId)
            .ToDictionary(group => group.Key, group => group.Last().Visibility);
        _displayRules = objectRules
            .GroupBy(rule => RuleTarget.From(rule.Selector))
            .ToDictionary(group => group.Key, group => group.Last());
        _displayModes = snapshot.DisplayModes
            .Where(item => item.Key != Guid.Empty && !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .GroupBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);
        _bulkDisplayMode = new FilteredPicker(
            new[] { Inherit }.Concat(_displayModes.Keys),
            "Display mode or Inherit",
            popupHeight: 280);
        _bulkDisplayMode.Text = Inherit;
        _bulkDisplayMode.SelectionCommitted += (_, _) =>
            ApplyDisplayMode(SelectedRows().Select(row => row.Target), _bulkDisplayMode.Text);

        _tree = new TreeGridView
        {
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            ShowHeader = true,
            RowHeight = 27,
            GridLines = GridLines.None,
            BackgroundColor = FoundryTheme.CanvasOverlayBackground,
        };
        _tree.Columns.Add(new GridColumn
        {
            HeaderText = "Item",
            DataCell = new ImageTextCell(nameof(AppearanceRow.Icon), nameof(AppearanceRow.ItemText)),
            // Keep the complete property region within the minimum dialog width so
            // the identity column never scrolls out of view.
            Width = 276,
            MinWidth = 240,
        });
        _visibilityColumn = new GridColumn
        {
            HeaderText = "Visibility",
            DataCell = new TextBoxCell(nameof(AppearanceRow.VisibilityText)),
            Width = 96,
        };
        _tree.Columns.Add(_visibilityColumn);
        _displayModeColumn = new GridColumn
        {
            HeaderText = "Display mode",
            DataCell = CreateDisplayModeCell(),
            Width = 190,
        };
        _tree.Columns.Add(_displayModeColumn);
        _tree.Columns.Add(new GridColumn
        {
            HeaderText = "Status",
            DataCell = new TextBoxCell(nameof(AppearanceRow.StatusText)),
            Width = 180,
        });
        _tree.CellFormatting += OnCellFormatting;
        _tree.MouseDown += OnMouseDown;
        _tree.CellClick += OnCellClick;
        _search.TextChanged += (_, _) => Reload();

        var inherit = new FoundryDialogButton(Inherit, FoundryDialogButtonStyle.Secondary, 82);
        var on = new FoundryDialogButton("On", FoundryDialogButtonStyle.Secondary, 60);
        var off = new FoundryDialogButton("Off", FoundryDialogButtonStyle.Secondary, 60);
        inherit.Click += (_, _) => ApplyVisibility(null);
        on.Click += (_, _) => ApplyVisibility(LayerVisibilityOverride.Visible);
        off.Click += (_, _) => ApplyVisibility(LayerVisibilityOverride.Hidden);

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new FoundryFormField(_search),
                new StackLayoutItem(_tree, true),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Items =
                    {
                        inherit,
                        on,
                        off,
                        FoundryTheme.VerticalRule(),
                        new StackLayoutItem(_bulkDisplayMode, true),
                    },
                },
            },
        };
        Reload();
    }

    internal IReadOnlyList<LayerVisibilityRule> LayerRules => _visibility
        .Where(pair => _snapshot.LayerSnapshots.ContainsKey(pair.Key))
        .Select(pair => new LayerVisibilityRule(
            new LayerReference(pair.Key, _snapshot.LayerSnapshots[pair.Key].FullPath), pair.Value))
        .OrderBy(rule => rule.Layer.FullPath, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal IReadOnlyList<ObjectDisplayRule> ObjectDisplayRules => _displayRules.Values
        .OrderBy(rule => rule.Selector.Kind)
        .ThenBy(rule => rule.Selector.LayerFullPath, StringComparer.OrdinalIgnoreCase)
        .ThenBy(rule => rule.Selector.ObjectId)
        .ToArray();

    private CustomCell CreateDisplayModeCell()
    {
        return new CustomCell
        {
            GetIdentifier = args => args.Item is AppearanceRow row && _editingTarget == row.Target
                ? "appearance-display-editor"
                : "appearance-display-label",
            CreateCell = args =>
            {
                if (args.Item is AppearanceRow row && _editingTarget == row.Target)
                {
                    FilteredPicker? picker = null;
                    picker = new FilteredPicker(
                        new[] { Inherit }.Concat(_displayModes.Keys),
                        "Search display modes",
                        popupHeight: 300,
                        controlHeight: 24);
                    picker.SelectionCommitted += (_, _) =>
                    {
                        if (!ReferenceEquals(_activePicker, picker)) return;
                        ApplyDisplayMode(_editingTargets, picker.Text);
                        CloseCellPicker();
                    };
                    picker.DismissRequested += (_, _) =>
                    {
                        if (ReferenceEquals(_activePicker, picker)) CloseCellPicker();
                    };
                    return picker;
                }

                return new Label { VerticalAlignment = VerticalAlignment.Center };
            },
            ConfigureCell = (args, control) =>
            {
                if (args.Item is not AppearanceRow row) return;
                if (control is Label label)
                {
                    label.Text = row.DisplayModeText;
                    label.TextColor = args.CellTextColor;
                    return;
                }

                if (control is not FilteredPicker picker || _editingTarget != row.Target) return;
                picker.Text = row.DisplayModeText;
                _activePicker = picker;
            },
        };
    }

    private void OnCellFormatting(object? sender, GridCellFormatEventArgs args)
    {
        if (args.Item is not AppearanceRow row) return;
        if (row.IsObject && ReferenceEquals(args.Column, _visibilityColumn))
            args.ForegroundColor = FoundryTheme.MutedText;
        if (_tree.SelectedItems.OfType<AppearanceRow>().Any(item => item.Target == row.Target))
        {
            args.BackgroundColor = SystemColors.Selection;
            args.ForegroundColor = SystemColors.SelectionText;
        }
        else if (row.IsLayer)
        {
            args.BackgroundColor = FoundryTheme.HierarchyFolderBackground;
        }
    }

    private void OnMouseDown(object? sender, MouseEventArgs args)
    {
        _propertyTargets = [];
        if ((args.Buttons & MouseButtons.Primary) == 0 || args.Modifiers != Keys.None) return;
        var cell = _tree.GetCellAt(args.Location);
        if (cell.Item is not AppearanceRow row || cell.Column is null ||
            !ReferenceEquals(cell.Column, _visibilityColumn) &&
            !ReferenceEquals(cell.Column, _displayModeColumn)) return;
        var selected = SelectedRows().Select(item => item.Target).ToArray();
        _propertyTargets = selected.Contains(row.Target) ? selected : [row.Target];
    }

    private void OnCellClick(object? sender, GridCellMouseEventArgs args)
    {
        if ((args.Buttons & MouseButtons.Primary) == 0 || args.Item is not AppearanceRow row)
            return;

        if (args.Modifiers != Keys.None)
        {
            _armedPropertyTarget = row.Target;
            return;
        }

        var isPropertyColumn = ReferenceEquals(args.GridColumn, _visibilityColumn) ||
                               ReferenceEquals(args.GridColumn, _displayModeColumn);
        if (!isPropertyColumn)
        {
            _armedPropertyTarget = row.Target;
            return;
        }

        var wasArmed = _armedPropertyTarget == row.Target;
        _armedPropertyTarget = row.Target;
        if (!wasArmed) return;

        if (ReferenceEquals(args.GridColumn, _visibilityColumn))
        {
            if (row.IsObject) return;
            var inherit = new ButtonMenuItem { Text = Inherit };
            var on = new ButtonMenuItem { Text = "On" };
            var off = new ButtonMenuItem { Text = "Off" };
            var targets = _propertyTargets.ToArray();
            inherit.Click += (_, _) => ApplyVisibility(null, targets);
            on.Click += (_, _) => ApplyVisibility(LayerVisibilityOverride.Visible, targets);
            off.Click += (_, _) => ApplyVisibility(LayerVisibilityOverride.Hidden, targets);
            new ContextMenu(inherit, on, off).Show(_tree, args.Location);
            return;
        }

        if (!ReferenceEquals(args.GridColumn, _displayModeColumn)) return;
        _editingTarget = row.Target;
        _editingTargets = _propertyTargets.ToArray();
        _activePicker = null;
        _tree.ReloadItem(row, reloadChildren: false);
        Application.Instance.AsyncInvoke(() => _activePicker?.OpenResults());
    }

    private void ApplyVisibility(LayerVisibilityOverride? value, IEnumerable<RuleTarget>? targets = null)
    {
        var layerIds = (targets ?? SelectedRows().Select(row => row.Target))
            .Where(target => target.Kind == RuleTargetKind.Layer)
            .Select(target => target.Id)
            .Distinct();
        foreach (var layerId in layerIds)
        {
            if (value is { } authored) _visibility[layerId] = authored;
            else _visibility.Remove(layerId);
        }
        RefreshVisibleRows();
    }

    private void ApplyDisplayMode(IEnumerable<RuleTarget> targets, string label)
    {
        var distinct = targets.Distinct().ToArray();
        if (string.Equals(label.Trim(), Inherit, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var target in distinct) _displayRules.Remove(target);
            RefreshVisibleRows();
            return;
        }

        if (!_displayModes.TryGetValue(label.Trim(), out var modeId)) return;
        foreach (var target in distinct)
        {
            var selector = target.Kind == RuleTargetKind.Layer
                ? new ObjectDisplaySelector(
                    ObjectDisplaySelectorKind.Layer,
                    LayerId: target.Id,
                    LayerFullPath: _snapshot.LayerSnapshots.GetValueOrDefault(target.Id)?.FullPath)
                : new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: target.Id);
            _displayRules[target] = new ObjectDisplayRule(selector, modeId, label.Trim());
        }
        RefreshVisibleRows();
    }

    private void CloseCellPicker()
    {
        var target = _editingTarget;
        _editingTarget = null;
        _activePicker?.CloseResults();
        _activePicker = null;
        _editingTargets = [];
        if (target is null) return;
        var row = Flatten(_roots).FirstOrDefault(item => item.Target == target.Value);
        if (row is not null) _tree.ReloadItem(row, reloadChildren: false);
    }

    private AppearanceRow[] SelectedRows() => _tree.SelectedItems
        .OfType<AppearanceRow>()
        .ToArray();

    private void Reload()
    {
        var expanded = Flatten(_roots).Where(row => row.Expanded)
            .Select(row => row.Target).ToHashSet();
        var query = _search.Text.Trim();
        var layers = _snapshot.LayerSnapshots.Values.ToDictionary(layer => layer.Id);
        var objectsByLayer = _snapshot.ModelObjects.Values
            .GroupBy(item => item.LayerId)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        var childrenByLayer = layers.Values
            .Where(layer => layer.ParentId is { } parent && layers.ContainsKey(parent))
            .GroupBy(layer => layer.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase).ToArray());

        AppearanceRow? BuildLayer(LayerSnapshot layer, IReadOnlyList<string> hiddenAncestors)
        {
            var target = new RuleTarget(RuleTargetKind.Layer, layer.Id);
            var hiddenBy = hiddenAncestors.FirstOrDefault();
            var nextHidden = hiddenAncestors.ToList();
            if (_visibility.TryGetValue(layer.Id, out var authoredVisibility) &&
                authoredVisibility == LayerVisibilityOverride.Hidden)
                nextHidden.Add(layer.FullPath);
            var children = new List<AppearanceRow>();
            if (childrenByLayer.TryGetValue(layer.Id, out var childLayers))
                foreach (var child in childLayers)
                    if (BuildLayer(child, nextHidden) is { } childRow) children.Add(childRow);
            if (objectsByLayer.TryGetValue(layer.Id, out var objects))
                foreach (var item in objects)
                {
                    var objectTarget = new RuleTarget(RuleTargetKind.Object, item.Id);
                    var objectMatches = query.Length == 0 ||
                        item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        item.LayerFullPath.Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (!objectMatches) continue;
                    children.Add(new AppearanceRow(
                        objectTarget,
                        string.IsNullOrWhiteSpace(item.Name) ? "Unnamed object" : item.Name,
                        item.LayerFullPath,
                        isLayer: false,
                        "—",
                        _displayRules.GetValueOrDefault(objectTarget)?.DisplayModeName ?? Inherit,
                        nextHidden.Count == 0 ? string.Empty : $"Hidden by {nextHidden[0]}"));
                }
            var matches = query.Length == 0 ||
                layer.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!matches && children.Count == 0) return null;
            var row = new AppearanceRow(
                target,
                LayerName(layer.FullPath),
                layer.FullPath,
                isLayer: true,
                _visibility.TryGetValue(layer.Id, out authoredVisibility) ? authoredVisibility switch
                {
                    LayerVisibilityOverride.Visible => "On",
                    LayerVisibilityOverride.Hidden => "Off",
                    _ => Inherit,
                } : Inherit,
                _displayRules.GetValueOrDefault(target)?.DisplayModeName ?? Inherit,
                hiddenBy is null ? string.Empty : $"Hidden by {hiddenBy}");
            foreach (var child in children) row.Children.Add(child);
            row.Expanded = query.Length > 0 || expanded.Contains(target);
            return row;
        }

        _roots = layers.Values
            .Where(layer => layer.ParentId is null || !layers.ContainsKey(layer.ParentId.Value))
            .OrderBy(layer => layer.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(layer => BuildLayer(layer, []))
            .OfType<AppearanceRow>()
            .ToArray();
        _tree.DataStore = new TreeGridItemCollection(_roots);
    }

    private void RefreshVisibleRows()
    {
        void Refresh(AppearanceRow row, IReadOnlyList<string> hiddenAncestors)
        {
            if (row.IsLayer)
            {
                row.VisibilityText = _visibility.TryGetValue(row.Target.Id, out var authoredVisibility)
                    ? authoredVisibility switch
                {
                    LayerVisibilityOverride.Visible => "On",
                    LayerVisibilityOverride.Hidden => "Off",
                    _ => Inherit,
                }
                    : Inherit;
            }
            row.DisplayModeText = _displayRules.GetValueOrDefault(row.Target)?.DisplayModeName ?? Inherit;
            row.StatusText = hiddenAncestors.Count == 0 ? string.Empty : $"Hidden by {hiddenAncestors[0]}";

            var nextHidden = hiddenAncestors.ToList();
            if (row.IsLayer && _visibility.TryGetValue(row.Target.Id, out var childVisibility) &&
                childVisibility == LayerVisibilityOverride.Hidden)
                nextHidden.Add(row.FullPath);
            foreach (var child in row.Children.OfType<AppearanceRow>())
                Refresh(child, nextHidden);
        }

        foreach (var root in _roots) Refresh(root, []);
        _tree.ReloadData();
    }

    private static string LayerName(string path)
    {
        var index = path.LastIndexOf("::", StringComparison.Ordinal);
        return index >= 0 ? path[(index + 2)..] : path;
    }

    private static IEnumerable<AppearanceRow> Flatten(IEnumerable<AppearanceRow> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children.OfType<AppearanceRow>())) yield return child;
        }
    }

    private enum RuleTargetKind { Layer, Object }

    private readonly record struct RuleTarget(RuleTargetKind Kind, Guid Id)
    {
        internal static RuleTarget From(ObjectDisplaySelector selector) =>
            selector.Kind == ObjectDisplaySelectorKind.Layer
                ? new RuleTarget(RuleTargetKind.Layer, selector.LayerId ?? Guid.Empty)
                : new RuleTarget(RuleTargetKind.Object, selector.ObjectId ?? Guid.Empty);
    }

    private sealed class AppearanceRow(
        RuleTarget target,
        string itemText,
        string fullPath,
        bool isLayer,
        string visibilityText,
        string displayModeText,
        string statusText) : TreeGridItem
    {
        internal RuleTarget Target { get; } = target;
        public string ItemText { get; } = itemText;
        internal string FullPath { get; } = fullPath;
        public Image Icon => IsLayer ? FoundryHierarchyIcons.Folder : FoundryHierarchyIcons.Object;
        public bool IsLayer { get; } = isLayer;
        public bool IsObject => !IsLayer;
        public string VisibilityText { get; set; } = visibilityText;
        public string DisplayModeText { get; set; } = displayModeText;
        public string StatusText { get; set; } = statusText;
    }
}
