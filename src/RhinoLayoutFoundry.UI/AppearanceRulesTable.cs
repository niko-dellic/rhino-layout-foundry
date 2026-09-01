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
    private RuleTarget? _handledPropertyTarget;
    private RuleTarget[] _propertyTargets = [];
    private RuleTarget[] _editingTargets = [];
    private readonly HashSet<RuleTarget> _pickedTargets = [];
    private readonly HashSet<RuleTarget> _dragHighlightedTargets = [];
    private RuleTarget? _dragAnchorTarget;
    private readonly Dictionary<RuleTarget, AppearanceVisibilityCell> _visibilityCells = [];

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
            ApplyDisplayMode(SelectedTargets(), _bulkDisplayMode.Text);

        _tree = new TreeGridView
        {
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            AllowColumnReordering = false,
            ShowHeader = true,
            RowHeight = 24,
            GridLines = GridLines.None,
            BackgroundColor = FoundryTheme.ContentBackground,
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
            DataCell = CreateVisibilityCell(),
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
        _tree.CellFormatting += OnCellFormatting;
        _tree.MouseDown += OnMouseDown;
        _tree.MouseMove += OnMouseMove;
        _tree.MouseUp += OnMouseUp;
        _tree.CellClick += OnCellClick;
        _search.TextChanged += (_, _) =>
        {
            _pickedTargets.Clear();
            Reload();
        };

        var on = new FoundryToolbarIconButton(
            FoundryViewIcons.VisibilityOn(), "Set selected layers On");
        var off = new FoundryToolbarIconButton(
            FoundryViewIcons.VisibilityOff(), "Set selected layers Off");
        var pickObjects = new FoundryToolbarIconButton(
            FoundryViewIcons.SceneCursor(), "Pick objects from the Rhino viewport");
        on.Click += (_, _) => ApplyVisibility(LayerVisibilityOverride.Visible);
        off.Click += (_, _) => ApplyVisibility(LayerVisibilityOverride.Hidden);
        pickObjects.Click += (_, _) => PickObjectsRequested?.Invoke(this, EventArgs.Empty);

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
                        on,
                        off,
                        FoundryTheme.VerticalRule(),
                        pickObjects,
                        FoundryTheme.VerticalRule(),
                        new StackLayoutItem(_bulkDisplayMode, true),
                    },
                },
            },
        };
        Reload();
    }

    private CustomCell CreateVisibilityCell() => new()
    {
        CreateCell = _ => new AppearanceVisibilityCell
        {
            Font = FoundryTheme.HierarchyTableFont,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
        ConfigureCell = (args, control) =>
        {
            if (args.Item is not AppearanceRow row || control is not AppearanceVisibilityCell cell)
                return;
            cell.Target = row.Target;
            cell.Text = row.VisibilityText;
            var selected = _dragHighlightedTargets.Contains(row.Target) ||
                           _pickedTargets.Contains(row.Target) ||
                           args.CellState.HasFlag(CellStates.Selected);
            cell.BackgroundColor = RowBackground(args.Row, selected);
            cell.TextColor = selected ? SystemColors.SelectionText : args.CellTextColor;
            _visibilityCells[row.Target] = cell;
        },
    };

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

    internal event EventHandler? PickObjectsRequested;

    internal void SelectObjects(IEnumerable<Guid> objectIds)
    {
        ArgumentNullException.ThrowIfNull(objectIds);
        var targets = objectIds.Distinct()
            .Select(id => new RuleTarget(RuleTargetKind.Object, id))
            .Where(target => _snapshot.ModelObjects.ContainsKey(target.Id))
            .ToHashSet();
        if (targets.Count == 0) return;

        if (_search.Text.Length > 0)
        {
            _search.Text = string.Empty;
            Reload();
        }
        _pickedTargets.Clear();
        _pickedTargets.UnionWith(targets);

        bool ExpandToPicked(AppearanceRow row)
        {
            var contains = _pickedTargets.Contains(row.Target);
            foreach (var child in row.Children.OfType<AppearanceRow>())
                contains |= ExpandToPicked(child);
            if (contains && row.IsLayer) row.Expanded = true;
            return contains;
        }

        foreach (var root in _roots) ExpandToPicked(root);
        _tree.ReloadData();
        var first = Flatten(_roots).FirstOrDefault(row => _pickedTargets.Contains(row.Target));
        if (first is not null) _tree.SelectedItem = first;
    }

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

                return new Label
                {
                    Font = FoundryTheme.HierarchyTableFont,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            },
            ConfigureCell = (args, control) =>
            {
                if (args.Item is not AppearanceRow row) return;
                if (control is Label label)
                {
                    label.Text = row.DisplayModeText;
                    var selected = _dragHighlightedTargets.Contains(row.Target) ||
                                   _pickedTargets.Contains(row.Target) ||
                                   args.CellState.HasFlag(CellStates.Selected);
                    label.BackgroundColor = RowBackground(args.Row, selected);
                    label.TextColor = selected ? SystemColors.SelectionText : args.CellTextColor;
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
        args.Font = FoundryTheme.HierarchyTableFont;
        if (row.IsObject && ReferenceEquals(args.Column, _visibilityColumn))
            args.ForegroundColor = FoundryTheme.MutedText;
        if (_dragHighlightedTargets.Contains(row.Target) ||
            _pickedTargets.Contains(row.Target) ||
            _tree.SelectedItems.OfType<AppearanceRow>().Any(item => item.Target == row.Target))
        {
            args.BackgroundColor = SystemColors.Selection;
            args.ForegroundColor = SystemColors.SelectionText;
        }
        else
            args.BackgroundColor = RowBackground(args.Row, selected: false);
    }

    private static Color RowBackground(int row, bool selected) => selected
        ? SystemColors.Selection
        : row % 2 == 0
            ? FoundryTheme.ContentBackground
            : FoundryTheme.HierarchyAlternateRowBackground;

    private void OnMouseDown(object? sender, MouseEventArgs args)
    {
        _propertyTargets = [];
        if ((args.Buttons & MouseButtons.Primary) == 0) return;
        var cell = _tree.GetCellAt(args.Location);
        if (cell.Item is not AppearanceRow row) return;
        var isPropertyColumn = ReferenceEquals(cell.Column, _visibilityColumn) ||
                               ReferenceEquals(cell.Column, _displayModeColumn);
        if (args.Modifiers == Keys.None && !isPropertyColumn)
            BeginDragSelectionPreview(row);
        if (args.Modifiers != Keys.None || !isPropertyColumn || cell.Column is null) return;
        var selected = _pickedTargets.Contains(row.Target)
            ? _pickedTargets.ToArray()
            : SelectedRows().Select(item => item.Target).ToArray();
        _propertyTargets = selected.Contains(row.Target) ? selected : [row.Target];
        if (!selected.Contains(row.Target)) return;

        // A native tree normally collapses a multi-selection to the clicked row
        // on mouse-up. Property cells operate on the existing selection, so own
        // this click before the native selection gesture can run.
        args.Handled = true;
        _handledPropertyTarget = row.Target;
        var wasArmed = _armedPropertyTarget == row.Target;
        _armedPropertyTarget = row.Target;
        if (wasArmed) ActivateProperty(row, cell.Column, _propertyTargets);
        Application.Instance.AsyncInvoke(() => _handledPropertyTarget = null);
    }

    private void OnMouseMove(object? sender, MouseEventArgs args)
    {
        if ((args.Buttons & MouseButtons.Primary) == 0 || _dragAnchorTarget is null) return;
        if (_tree.GetCellAt(args.Location).Item is AppearanceRow row)
            UpdateDragSelectionPreview(row);
    }

    private void OnMouseUp(object? sender, MouseEventArgs args)
    {
        if (_dragAnchorTarget is null) return;
        var previewTargets = _dragHighlightedTargets.ToArray();
        _dragAnchorTarget = null;
        _dragHighlightedTargets.Clear();
        // The native tree finalizes its range selection after MouseUp. Refresh
        // on the next UI turn so the preview hands off without a visual gap.
        Application.Instance.AsyncInvoke(() => ReloadTargets(previewTargets));
    }

    private void BeginDragSelectionPreview(AppearanceRow row)
    {
        _dragAnchorTarget = row.Target;
        _dragHighlightedTargets.Clear();
        _dragHighlightedTargets.Add(row.Target);
        _tree.ReloadItem(row, reloadChildren: false);
    }

    private void UpdateDragSelectionPreview(AppearanceRow current)
    {
        if (_dragAnchorTarget is not { } anchor) return;
        var visibleRows = VisibleRows(_roots).ToArray();
        var anchorIndex = Array.FindIndex(visibleRows, row => row.Target == anchor);
        var currentIndex = Array.FindIndex(visibleRows, row => row.Target == current.Target);
        if (anchorIndex < 0 || currentIndex < 0) return;
        var first = Math.Min(anchorIndex, currentIndex);
        var last = Math.Max(anchorIndex, currentIndex);
        var nextTargets = visibleRows[first..(last + 1)].Select(row => row.Target).ToHashSet();
        if (_dragHighlightedTargets.SetEquals(nextTargets)) return;

        var changedTargets = _dragHighlightedTargets.Except(nextTargets)
            .Concat(nextTargets.Except(_dragHighlightedTargets))
            .ToArray();
        _dragHighlightedTargets.Clear();
        _dragHighlightedTargets.UnionWith(nextTargets);
        ReloadTargets(changedTargets);
    }

    private void ReloadTargets(IEnumerable<RuleTarget> targets)
    {
        var targetSet = targets.ToHashSet();
        foreach (var row in Flatten(_roots).Where(row => targetSet.Contains(row.Target)))
            _tree.ReloadItem(row, reloadChildren: false);
    }

    private void OnCellClick(object? sender, GridCellMouseEventArgs args)
    {
        if ((args.Buttons & MouseButtons.Primary) == 0 || args.Item is not AppearanceRow row)
            return;

        if (_handledPropertyTarget == row.Target)
        {
            _handledPropertyTarget = null;
            return;
        }

        if (_pickedTargets.Count > 0 && !_pickedTargets.Contains(row.Target))
        {
            _pickedTargets.Clear();
            _tree.ReloadData();
        }

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

        ActivateProperty(row, args.GridColumn, _propertyTargets);
    }

    private void ActivateProperty(
        AppearanceRow row,
        GridColumn column,
        IEnumerable<RuleTarget> targets)
    {
        if (ReferenceEquals(column, _visibilityColumn))
        {
            if (row.IsObject) return;
            var next = row.VisibilityText == "○"
                ? LayerVisibilityOverride.Visible
                : LayerVisibilityOverride.Hidden;
            ApplyVisibility(next, targets);
            return;
        }

        if (!ReferenceEquals(column, _displayModeColumn)) return;
        _editingTarget = row.Target;
        _editingTargets = targets.ToArray();
        _activePicker = null;
        _tree.ReloadItem(row, reloadChildren: false);
        Application.Instance.AsyncInvoke(() => _activePicker?.OpenResults());
    }

    private void ApplyVisibility(LayerVisibilityOverride value, IEnumerable<RuleTarget>? targets = null)
    {
        var layerIds = (targets ?? SelectedTargets())
            .Where(target => target.Kind == RuleTargetKind.Layer)
            .Select(target => target.Id)
            .Distinct();
        foreach (var layerId in layerIds)
            _visibility[layerId] = value;
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

    private IReadOnlyList<RuleTarget> SelectedTargets() => _pickedTargets.Count > 0
        ? _pickedTargets.ToArray()
        : SelectedRows().Select(row => row.Target).ToArray();

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
            var ownVisible = IsLayerOn(layer);
            if (!ownVisible)
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
                        _displayRules.GetValueOrDefault(objectTarget)?.DisplayModeName ?? Inherit));
                }
            var matches = query.Length == 0 ||
                layer.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!matches && children.Count == 0) return null;
            var row = new AppearanceRow(
                target,
                LayerName(layer.FullPath),
                layer.FullPath,
                isLayer: true,
                VisibilityGlyph(ownVisible, hiddenBy is not null),
                _displayRules.GetValueOrDefault(target)?.DisplayModeName ?? Inherit);
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
        var changedDisplayRows = new List<AppearanceRow>();

        void Refresh(AppearanceRow row, IReadOnlyList<string> hiddenAncestors)
        {
            var previousVisibility = row.VisibilityText;
            var previousDisplayMode = row.DisplayModeText;
            if (row.IsLayer)
            {
                var layer = _snapshot.LayerSnapshots[row.Target.Id];
                row.VisibilityText = VisibilityGlyph(IsLayerOn(layer), hiddenAncestors.Count > 0);
            }
            row.DisplayModeText = _displayRules.GetValueOrDefault(row.Target)?.DisplayModeName ?? Inherit;
            if (!string.Equals(previousVisibility, row.VisibilityText, StringComparison.Ordinal) &&
                _visibilityCells.GetValueOrDefault(row.Target) is { } visibilityCell &&
                visibilityCell.Target == row.Target)
            {
                visibilityCell.Text = row.VisibilityText;
                visibilityCell.Invalidate();
            }
            if (!string.Equals(previousDisplayMode, row.DisplayModeText, StringComparison.Ordinal))
                changedDisplayRows.Add(row);

            var nextHidden = hiddenAncestors.ToList();
            if (row.IsLayer && !IsLayerOn(_snapshot.LayerSnapshots[row.Target.Id]))
                nextHidden.Add(row.FullPath);
            foreach (var child in row.Children.OfType<AppearanceRow>())
                Refresh(child, nextHidden);
        }

        foreach (var root in _roots) Refresh(root, []);
        foreach (var row in changedDisplayRows)
            _tree.ReloadItem(row, reloadChildren: false);
    }

    private static string LayerName(string path)
    {
        var index = path.LastIndexOf("::", StringComparison.Ordinal);
        return index >= 0 ? path[(index + 2)..] : path;
    }

    private bool IsLayerOn(LayerSnapshot layer) =>
        _visibility.TryGetValue(layer.Id, out var authoredVisibility)
            ? authoredVisibility == LayerVisibilityOverride.Visible
            : layer.IsGloballyVisible;

    private static string VisibilityGlyph(bool ownVisible, bool hiddenByAncestor) =>
        !ownVisible ? "○" : hiddenByAncestor ? "◐" : "●";

    private static IEnumerable<AppearanceRow> Flatten(IEnumerable<AppearanceRow> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Flatten(root.Children.OfType<AppearanceRow>())) yield return child;
        }
    }

    private static IEnumerable<AppearanceRow> VisibleRows(IEnumerable<AppearanceRow> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            if (!root.Expanded) continue;
            foreach (var child in VisibleRows(root.Children.OfType<AppearanceRow>()))
                yield return child;
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
        string displayModeText) : TreeGridItem
    {
        internal RuleTarget Target { get; } = target;
        public string ItemText { get; } = itemText;
        internal string FullPath { get; } = fullPath;
        public Image Icon => IsLayer ? FoundryHierarchyIcons.Layer : FoundryHierarchyIcons.Object;
        public bool IsLayer { get; } = isLayer;
        public bool IsObject => !IsLayer;
        public string VisibilityText { get; set; } = visibilityText;
        public string DisplayModeText { get; set; } = displayModeText;
    }

    private sealed class AppearanceVisibilityCell : Label
    {
        internal RuleTarget Target { get; set; }
    }
}
