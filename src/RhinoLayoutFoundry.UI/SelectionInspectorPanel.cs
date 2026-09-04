using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal enum SelectionInspectorContent
{
    All,
    Appearance,
}

internal sealed class SelectionInspectorPanel : Panel
{
    internal const int OverlayWidth = 344;
    private const string Mixed = "Mixed";
    private const string CustomPaperPreset = "Custom";
    private const int NamedViewThumbnailMinimum = 64;
    private const int NamedViewThumbnailMaximum = 240;
    private const int NamedViewThumbnailDefault = 128;
    private static readonly string[] Units = ["Millimeters", "Centimeters", "Meters", "Inches", "Feet"];

    private readonly Label _selectionSummary = FoundryTheme.MutedLabel();
    private readonly TextBox _name = new();
    private readonly TextArea _notes = new() { Height = 72, Wrap = true };
    private readonly FoundryDialogButton _saveNotes = new("Save notes", FoundryDialogButtonStyle.Secondary, 100);
    private readonly Label _notesMixed = FoundryTheme.MutedLabel("Mixed notes — saving replaces all selected notes.");
    private readonly Label _selectionError = ErrorLabel();
    private readonly Panel _selectionSection;
    private readonly FoundryCheckBox _print = new("Include in Print all");
    private readonly DropDown _paperPreset = new();
    private readonly NumericStepper _paperWidth = DimensionStepper();
    private readonly NumericStepper _paperHeight = DimensionStepper();
    private readonly DropDown _paperUnit = new();
    private readonly Label _paperMixed = FoundryTheme.MutedLabel("Mixed — enter a complete size to apply to all affected layouts.");
    private readonly FilteredPicker _titleBlock = new([], "Search title blocks");
    private readonly FoundryTextSegmentedControl _titleBlockMode = new(["None", "Right", "Bottom"], 0, 72);
    private readonly FoundryCheckBox _templateRegistration = new("Use as layout template");
    private readonly Label _templateError = ErrorLabel();
    private readonly Panel _templateSection;
    private readonly TextArea _revisions = new() { Height = 82, Wrap = false };
    private readonly FoundryDialogButton _revisionAction = new("Save", FoundryDialogButtonStyle.Secondary, 92);
    private readonly Label _layoutError = ErrorLabel();
    private readonly Panel _layoutSection;
    private readonly FilteredPicker _displayMode = new([], "Search display modes");
    private readonly Label _detailError = ErrorLabel();
    private readonly Panel _detailSection;
    private readonly FilteredPicker _appearanceState = new([], "Search appearance states");
    private readonly FoundryDialogButton _assignAppearanceState = new("Assign", FoundryDialogButtonStyle.Secondary, 76);
    private readonly FoundryDialogButton _inheritAppearanceState = new("Use inherited", FoundryDialogButtonStyle.Secondary, 104);
    private readonly FoundryDialogButton _editAppearanceOverrides = new("Edit local overrides…", FoundryDialogButtonStyle.Secondary, 154);
    private readonly Label _appearanceError = ErrorLabel();
    private readonly Panel _appearanceSection;
    private readonly TextBox _layerSearch = new() { PlaceholderText = "Search layers" };
    private readonly FilteredPicker _layerTemplate = new([], "Search layer states");
    private readonly FoundryDialogButton _linkLayerTemplate = new("Assign", FoundryDialogButtonStyle.Secondary, 76);
    private readonly FoundryDialogButton _detachLayerTemplate = new("Use inherited", FoundryDialogButtonStyle.Secondary, 104);
    private readonly GridView _layers;
    private readonly FoundryDialogButton _layersInherit = new("Inherit", FoundryDialogButtonStyle.Secondary, 82);
    private readonly FoundryDialogButton _layersOn = new("On", FoundryDialogButtonStyle.Secondary, 64);
    private readonly FoundryDialogButton _layersOff = new("Off", FoundryDialogButtonStyle.Secondary, 64);
    private readonly FoundryDialogButton _clearLayerOverrides = new("Clear local overrides", FoundryDialogButtonStyle.Secondary, 150);
    private readonly Label _layersError = ErrorLabel();
    private readonly Panel _layersSection;
    private readonly GridView _objectRules;
    private readonly FilteredPicker _objectTemplate = new([], "Search object display states");
    private readonly FoundryDialogButton _linkObjectTemplate = new("Assign", FoundryDialogButtonStyle.Secondary, 76);
    private readonly FoundryDialogButton _detachObjectTemplate = new("Use inherited", FoundryDialogButtonStyle.Secondary, 104);
    private readonly FilteredPicker _objectTarget = new([], "Search model objects");
    private readonly FilteredPicker _objectLayer = new([], "Search layers");
    private readonly FilteredPicker _objectMode = new([], "Search display modes");
    private readonly FoundryCheckBox _includeChildLayers = new("Include child layers");
    private readonly FoundryDialogButton _addObjectRule = new("Add object", FoundryDialogButtonStyle.Secondary, 104);
    private readonly FoundryDialogButton _addLayerRule = new("Add layer", FoundryDialogButtonStyle.Secondary, 96);
    private readonly FoundryDialogButton _removeObjectRule = new("Remove selected", FoundryDialogButtonStyle.Secondary, 126);
    private readonly Label _objectsError = ErrorLabel();
    private readonly Panel _objectsSection;
    private readonly FoundryToolbarIconButton _namedViewListMode;
    private readonly FoundryToolbarIconButton _namedViewThumbnailMode;
    private readonly FoundryToolbarButtonGroup _namedViewModeGroup;
    private readonly FoundrySlider _namedViewThumbnailSize;
    private readonly Label _namedViewThumbnailSizeValue;
    private readonly Panel _namedViewThumbnailSizeRow;
    private readonly GridView _namedViews;
    private readonly NamedViewThumbnailGrid _namedViewThumbnailGrid;
    private readonly Scrollable _namedViewThumbnailBrowser;
    private readonly FoundryDialogButton _assignNamedView = new("Assign to selection", FoundryDialogButtonStyle.Secondary, 154);
    private readonly Label _namedViewError = ErrorLabel();
    private readonly Panel _namedViewSection;
    private DocumentSnapshot? _snapshot;
    private SelectionInspectorModel? _model;
    private IReadOnlyList<OverviewNodeKey> _selection = [];
    private PaperSizeChoice[] _paperChoices = PaperSizes;
    private Dictionary<string, Guid?> _titleBlockByLabel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Guid> _displayModeByLabel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _namedViewPreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Control> _busySections = [];
    private NamedViewRow[] _namedViewRows = [];
    private LayerRuleRow[] _layerRows = [];
    private ObjectRuleRow[] _objectRuleRows = [];
    private Dictionary<string, Guid> _objectIdByLabel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Guid> _layerIdByLabel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Guid> _layerTemplateByLabel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Guid> _objectTemplateByLabel = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Guid> _appearanceStateByLabel = new(StringComparer.OrdinalIgnoreCase);
    private ObserverPoint _namedViewPress;
    private bool _updating;
    private bool _paperCommitInProgress;
    private string? _selectedNamedView;

    internal SelectionInspectorPanel(SelectionInspectorContent contentMode = SelectionInspectorContent.All)
    {
        Width = contentMode == SelectionInspectorContent.All ? OverlayWidth : 720;
        BackgroundColor = FoundryTheme.CanvasOverlayBackground;
        Padding = new Padding(FoundryTheme.Space3);

        _selectionSection = Section("Selection", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _selectionSummary,
                Field("Name", InspectorField(_name)),
                Field("Notes", new FoundryFormField(_notes)),
                _notesMixed,
                _saveNotes,
                _selectionError,
            },
        });

        _templateSection = Section("Template registration", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
_templateRegistration,
                FoundryTheme.MutedLabel("Register this hierarchy item as a live capability source."),
                _templateError,
            },
        });

        _paperPreset.DataStore = new[] { CustomPaperPreset }
            .Concat(_paperChoices.Select(choice => choice.Label))
            .ToArray();
        _paperUnit.DataStore = Units;
        _selectionSummary.Wrap = WrapMode.Word;
        _paperMixed.Wrap = WrapMode.Word;
        _revisions.ToolTip = "One row per line: Code | Date | Description | Issued by | Checked by";
        _layoutSection = Section("Layouts", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _print,
                Field("Paper preset", InspectorField(_paperPreset)),
                _paperMixed,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    Items =
                    {
                        new StackLayoutItem(Field("Width", InspectorField(_paperWidth)), true),
                        new StackLayoutItem(Field("Height", InspectorField(_paperHeight)), true),
                    },
                },
                Field("Units", InspectorField(_paperUnit)),
                Field("Title block", _titleBlockMode),
                _layoutError,
            },
        });

        _detailSection = Section("Details", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Field("Display mode", _displayMode),
                _detailError,
            },
        });

        _appearanceSection = Section("Appearance", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Field("State basis", _appearanceState),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items = { _assignAppearanceState, _inheritAppearanceState },
                },
                _editAppearanceOverrides,
                FoundryTheme.MutedLabel("The assigned state is the basis; local rules override it without detaching."),
                _appearanceError,
            },
        });

        _layers = new GridView
        {
            ShowHeader = true,
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            Height = 196,
            RowHeight = 26,
            GridLines = GridLines.None,
            BackgroundColor = FoundryTheme.CanvasOverlayBackground,
        };
        _layers.Columns.Add(new GridColumn
        {
            HeaderText = "Layer",
            DataCell = new TextBoxCell(nameof(LayerRuleRow.Name)),
            AutoSize = true,
        });
        _layers.Columns.Add(new GridColumn
        {
            HeaderText = "State",
            DataCell = new TextBoxCell(nameof(LayerRuleRow.State)),
            Width = 72,
        });
        _layersSection = Section("Detail layers", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Field("State basis", _layerTemplate),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items = { _linkLayerTemplate, _detachLayerTemplate },
                },
                InspectorField(_layerSearch),
                _layers,
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items = { _layersInherit, _layersOn, _layersOff },
                },
                _clearLayerOverrides,
                FoundryTheme.MutedLabel("Folder and layout rules apply to descendant detail viewports. Child overrides are preserved."),
                _layersError,
            },
        });

        _objectRules = new GridView
        {
            ShowHeader = true,
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            Height = 156,
            RowHeight = 26,
            GridLines = GridLines.None,
            BackgroundColor = FoundryTheme.CanvasOverlayBackground,
        };
        _objectRules.Columns.Add(new GridColumn
        {
            HeaderText = "Target",
            DataCell = new TextBoxCell(nameof(ObjectRuleRow.Target)),
            AutoSize = true,
        });
        _objectRules.Columns.Add(new GridColumn
        {
            HeaderText = "Mode",
            DataCell = new TextBoxCell(nameof(ObjectRuleRow.Mode)),
            Width = 104,
        });
        _objectsSection = Section("Custom object display modes", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Field("State basis", _objectTemplate),
                new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = FoundryTheme.Space1,
                    Items = { _linkObjectTemplate, _detachObjectTemplate },
                },
                Field("Display mode", _objectMode),
                Field("Exact model object", _objectTarget),
                _addObjectRule,
                Field("Objects on layer", _objectLayer),
                _includeChildLayers,
                _addLayerRule,
                _objectRules,
                _removeObjectRule,
                FoundryTheme.MutedLabel("Exact objects override layer selectors. Missing imported object IDs remain visible as unresolved rules."),
                _objectsError,
            },
        });

        _namedViewListMode = new FoundryToolbarIconButton(
            FoundryViewIcons.ListView(), "Show named views as a list", isToggle: true)
        { Checked = true };
        _namedViewThumbnailMode = new FoundryToolbarIconButton(
            FoundryViewIcons.ThumbnailStack(), "Show named-view previews", isToggle: true);
        _namedViewModeGroup = new FoundryToolbarButtonGroup(
            _namedViewListMode,
            _namedViewThumbnailMode);
        _namedViewThumbnailSize = new FoundrySlider(
            NamedViewThumbnailMinimum,
            NamedViewThumbnailMaximum,
            NamedViewThumbnailDefault,
            width: 220,
            toolTipFormatter: value => $"Named-view tile width: {value} px");
        _namedViewThumbnailSizeValue = FoundryTheme.MutedLabel();
        _namedViewThumbnailSizeValue.Width = 46;
        _namedViewThumbnailSizeValue.TextAlignment = TextAlignment.Right;
        _namedViewThumbnailSizeRow = new Panel
        {
            Visible = false,
            Content = Field("Thumbnail size", new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space2,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(_namedViewThumbnailSize, true),
                    _namedViewThumbnailSizeValue,
                },
            }),
        };
        UpdateNamedViewThumbnailSizeLabel();
        _namedViews = new GridView
        {
            ShowHeader = false,
            Border = BorderType.None,
            AllowMultipleSelection = false,
            AllowEmptySelection = true,
            Height = 210,
            RowHeight = 28,
            GridLines = GridLines.None,
            BackgroundColor = FoundryTheme.CanvasOverlayBackground,
        };
        _namedViews.Columns.Add(new GridColumn
        {
            DataCell = new ImageTextCell(nameof(NamedViewRow.Image), nameof(NamedViewRow.Name)),
            AutoSize = true,
        });
        _namedViewThumbnailGrid = new NamedViewThumbnailGrid();
        _namedViewThumbnailBrowser = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = true,
            Height = 120,
            Visible = false,
            BackgroundColor = FoundryTheme.CanvasOverlayBackground,
            Content = _namedViewThumbnailGrid,
        };
        _namedViewSection = Section("Named views", new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _namedViewModeGroup,
                _namedViewThumbnailSizeRow,
                _namedViews,
                _namedViewThumbnailBrowser,
                _assignNamedView,
                _namedViewError,
            },
        });

        var content = new StackLayout
        {
            Padding = new Padding(0, 0, FoundryTheme.Space1, FoundryTheme.Space4),
            Spacing = FoundryTheme.Space4,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                _selectionSection,
                _templateSection,
                _layoutSection,
                _detailSection,
                _appearanceSection,
                _namedViewSection,
            },
        };
        Content = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = content,
        };

        if (contentMode != SelectionInspectorContent.All)
        {
            _selectionSection.Visible = false;
            _templateSection.Visible = false;
            _layoutSection.Visible = false;
            _detailSection.Visible = false;
            _appearanceSection.Visible = contentMode == SelectionInspectorContent.Appearance;
            _namedViewSection.Visible = false;
        }

        _name.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Enter) return;
            _ = CommitRenameAsync();
            eventArgs.Handled = true;
        };
        _name.LostFocus += (_, _) => _ = CommitRenameAsync();
        _saveNotes.Click += (_, _) => _ = CommitNotesAsync();
        _print.CheckedChanged += (_, _) =>
        {
            if (!_updating) _ = CommitPrintAsync();
        };
        _paperPreset.SelectedIndexChanged += (_, _) =>
        {
            if (_updating) return;
            var choiceIndex = _paperPreset.SelectedIndex - 1;
            if (choiceIndex >= 0 && choiceIndex < _paperChoices.Length)
            {
                var choice = _paperChoices[choiceIndex];
                _updating = true;
                _paperWidth.Value = choice.Width;
                _paperHeight.Value = choice.Height;
                _paperUnit.SelectedIndex = Array.FindIndex(Units,
                    unit => string.Equals(unit, choice.UnitSystem, StringComparison.OrdinalIgnoreCase));
                _updating = false;
                _ = CommitPaperAsync();
            }
        };
        WirePaperCommit(_paperWidth);
        WirePaperCommit(_paperHeight);
        _paperUnit.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating) _ = CommitPaperAsync();
        };
        _titleBlockMode.SelectedIndexChanged += (_, _) =>
        {
            if (!_updating) _ = CommitTitleBlockAsync();
        };
        _templateRegistration.CheckedChanged += (_, _) =>
        {
            if (!_updating) _ = CommitTemplateRegistrationAsync();
        };
        _displayMode.ValueChanged += (_, _) =>
        {
            if (!_updating && _displayModeByLabel.ContainsKey(_displayMode.Text.Trim()))
                _ = CommitDisplayModeAsync();
        };
        _namedViewListMode.Click += (_, _) => SetNamedViewMode(false);
        _namedViewThumbnailMode.Click += (_, _) => SetNamedViewMode(true);
        _namedViewThumbnailSize.ValueChanged += (_, _) =>
        {
            UpdateNamedViewThumbnailSizeLabel();
            UpdateNamedViewThumbnailLayout();
        };
        _namedViews.SelectedRowsChanged += (_, _) =>
        {
            var index = _namedViews.SelectedRow;
            _selectedNamedView = index >= 0 && index < _namedViewRows.Length
                ? _namedViewRows[index].Name
                : null;
            _assignNamedView.Enabled = _model?.AffectedDetailCount > 0 && _selectedNamedView is not null;
        };
        _namedViewThumbnailGrid.SelectionChanged += (_, eventArgs) =>
        {
            _selectedNamedView = eventArgs.Name;
            _namedViews.SelectedRow = Array.FindIndex(_namedViewRows,
                row => string.Equals(row.Name, _selectedNamedView, StringComparison.OrdinalIgnoreCase));
            _assignNamedView.Enabled = _model?.AffectedDetailCount > 0 && _selectedNamedView is not null;
        };
        _namedViewThumbnailBrowser.SizeChanged += (_, _) => UpdateNamedViewThumbnailLayout();
        _namedViews.MouseDown += (_, eventArgs) =>
            _namedViewPress = new ObserverPoint(eventArgs.Location.X, eventArgs.Location.Y);
        _namedViews.MouseMove += (_, eventArgs) =>
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary) || _selectedNamedView is null) return;
            var current = new ObserverPoint(eventArgs.Location.X, eventArgs.Location.Y);
            if (Math.Sqrt(Math.Pow(current.X - _namedViewPress.X, 2) + Math.Pow(current.Y - _namedViewPress.Y, 2)) <= 6)
                return;
            var data = new DataObject();
            data.SetString(_selectedNamedView, ObserverCanvasDrawable.NamedViewDragType);
            _namedViews.DoDragDrop(data, DragEffects.Copy);
        };
        _assignNamedView.Click += (_, _) => _ = CommitNamedViewAsync();
        _layerSearch.TextChanged += (_, _) => ReloadLayers();
        _assignAppearanceState.Click += (_, _) => _ = AssignStateAsync();
        _inheritAppearanceState.Click += (_, _) => _ = ClearStateAssignmentAsync();
        _editAppearanceOverrides.Click += (_, _) => _ = EditLocalAppearanceOverridesAsync();
        _layersInherit.Click += (_, _) => _ = CommitLayerVisibilityAsync(null);
        _layersOn.Click += (_, _) => _ = CommitLayerVisibilityAsync(LayerVisibilityOverride.Visible);
        _layersOff.Click += (_, _) => _ = CommitLayerVisibilityAsync(LayerVisibilityOverride.Hidden);
        _clearLayerOverrides.Click += (_, _) => _ = ClearLayerOverridesAsync();
        _addObjectRule.Click += (_, _) => _ = AddExactObjectRuleAsync();
        _addLayerRule.Click += (_, _) => _ = AddLayerObjectRuleAsync();
        _removeObjectRule.Click += (_, _) => _ = RemoveObjectRulesAsync();
        SetContext(null, []);
    }

    internal event EventHandler<SelectionInspectorOperationEventArgs>? OperationCompleted;
    internal event EventHandler? NamedViewPreviewsRequested;

    internal bool UsesNamedViewThumbnails => _namedViewThumbnailMode.Checked;

    internal IReadOnlyList<string> VisibleNamedViews() => UsesNamedViewThumbnails
        ? _namedViewRows.Select(row => row.Name).ToArray()
        : [];

    internal bool HasNamedViewPreview(string name) => _namedViewPreviews.ContainsKey(name);

    internal void SetNamedViewPreview(string name, Bitmap bitmap)
    {
        _namedViewPreviews[name] = bitmap;
        ReloadNamedViews();
    }

    internal void ClearNamedViewPreviews()
    {
        _namedViewPreviews.Clear();
        ReloadNamedViews();
    }

    internal void SetContext(DocumentSnapshot? snapshot, IEnumerable<OverviewNodeKey> selection)
    {
        _snapshot = snapshot;
        _selection = selection?.Distinct().ToArray() ?? [];
        _model = snapshot is null ? null : SelectionInspectorModel.Create(snapshot, _selection);
        Populate();
    }

    private void Populate()
    {
        _updating = true;
        ClearErrors();
        var model = _model;
        var snapshot = _snapshot;
        var enabled = model?.HasSelection == true && snapshot is not null;
        _selectionSummary.Text = model is null
            ? "No selection"
            : $"{model.SelectedFolderCount} folders · {model.SelectedLayoutCount} layouts · {model.SelectedDetailCount} details\n" +
              $"Affects {model.AffectedLayoutCount} layouts and {model.AffectedDetailCount} details";
        _name.Text = model?.RenameValue ?? string.Empty;
        _name.Enabled = model?.RenameTarget is not null;
        _notes.Text = model?.NotesValue ?? string.Empty;
        _notesMixed.Visible = model?.NotesIsMixed == true;
        _notes.Enabled = model?.EditableNotesTargets.Count > 0;
        _saveNotes.Enabled = model?.EditableNotesTargets.Count > 0;
        _selectionSection.Enabled = enabled;

        _layoutSection.Enabled = model?.AffectedLayoutCount > 0;
        _print.Checked = model?.PrintIsMixed == true ? null : model?.PrintIncluded;
        _paperWidth.Value = model?.PaperWidth ?? 0;
        _paperHeight.Value = model?.PaperHeight ?? 0;
        _paperMixed.Visible = model?.PaperIsMixed == true;
        _paperUnit.SelectedIndex = Array.FindIndex(Units,
            unit => string.Equals(unit, model?.PaperUnitSystem, StringComparison.OrdinalIgnoreCase));
        _paperPreset.SelectedIndex = FindPaperPreset(model);

        var titleBlockModes = model?.AffectedLayoutIds
            .Select(id => snapshot?.Sheets.GetValueOrDefault(id))
            .Where(sheet => sheet is not null)
            .Cast<SheetSnapshot>()
            .Select(TitleBlockModeIndex)
            .Distinct()
            .ToArray() ?? [];
        _titleBlockMode.SelectedIndex = titleBlockModes.Length == 1 && titleBlockModes[0] >= 0
            ? titleBlockModes[0]
            : 0;
        _titleBlockMode.ToolTip = titleBlockModes.Contains(-1)
            ? "The selected title-block mode is unavailable." : titleBlockModes.Length > 1 ? "The selected layouts use mixed title-block modes." : null;
        var selectedScopes = SelectedScopes().ToArray();
        var templateScopes = selectedScopes.Where(scope => scope.Kind is HierarchyScopeKind.Sheet or HierarchyScopeKind.Detail)
                .ToArray();
        var registrationValues = templateScopes.Select(scope => snapshot?.TemplateRegistrations
                .Any(item => item.Source == scope) == true)
            .Distinct().ToArray();
        _templateSection.Enabled = templateScopes.Length > 0 && templateScopes.Length == selectedScopes.Length;
        _templateRegistration.Checked = registrationValues.Length == 1
            ? registrationValues[0]
            : null;
        _templateRegistration.ToolTip = registrationValues.Length > 1
            ? "Mixed template registration." : null;
        var layouts = model?.AffectedLayoutIds.Select(id => snapshot?.Sheets.GetValueOrDefault(id))
            .Where(sheet => sheet is not null).Cast<SheetSnapshot>().ToArray() ?? [];
        _revisions.Text = layouts.Length == 1
            ? FormatRevisions(layouts[0].TitleBlockData?.Revisions ?? [])
            : string.Empty;
        _revisions.ToolTip = layouts.Length == 1
            ? "Code | Date | Description | Issued by | Checked by"
            : "One revision to append to every affected layout";
        _revisionAction.Text = layouts.Length == 1 ? "Save" : "Add";

        _detailSection.Enabled = model?.AffectedDetailCount > 0;
        _displayModeByLabel = snapshot?.DisplayModes
            .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase) ?? [];
        _displayMode.SetChoices(_displayModeByLabel.Keys);
        _displayMode.Text = model?.DisplayModeIsMixed == true
            ? Mixed
            : model?.DisplayModeId is { } modeId && snapshot?.DisplayModes.TryGetValue(modeId, out var modeName) == true
                ? modeName
                : string.Empty;

        _layerIdByLabel = snapshot?.LayerSnapshots.Values
            .OrderBy(layer => layer.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(layer => layer.FullPath, layer => layer.Id, StringComparer.OrdinalIgnoreCase) ?? [];
        _objectIdByLabel = snapshot?.ModelObjects.Values
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.LayerFullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(ObjectLabel, item => item.Id, StringComparer.OrdinalIgnoreCase) ?? [];
        _objectTarget.SetChoices(_objectIdByLabel.Keys);
        _objectLayer.SetChoices(_layerIdByLabel.Keys);
        _objectMode.SetChoices(_displayModeByLabel.Keys);
        if (!_objectMode.ContainsChoice(_objectMode.Text))
            _objectMode.Text = _displayModeByLabel.Keys.FirstOrDefault() ?? string.Empty;
        _appearanceSection.Enabled = selectedScopes.Length > 0;
        _appearanceStateByLabel = BuildStateChoices(snapshot);
        _appearanceState.SetChoices(_appearanceStateByLabel.Keys);
        _appearanceState.Text = AssignedStateLabel(selectedScopes, _appearanceStateByLabel);
        _inheritAppearanceState.Enabled = selectedScopes.Any(scope =>
            snapshot?.StateAssignments.Any(link => link.Target == scope) == true);

        var namedViewBrowsingEnabled = snapshot is not null;
        _namedViewSection.Enabled = namedViewBrowsingEnabled;
        _namedViewListMode.Enabled = namedViewBrowsingEnabled;
        _namedViewThumbnailMode.Enabled = namedViewBrowsingEnabled;
        _namedViewThumbnailSize.Enabled = namedViewBrowsingEnabled;
        _namedViews.Enabled = namedViewBrowsingEnabled && snapshot!.NamedViews.Count > 0;
        _namedViewThumbnailGrid.Enabled = namedViewBrowsingEnabled && snapshot!.NamedViews.Count > 0;
        ReloadNamedViews();
        _assignNamedView.Enabled = model?.AffectedDetailCount > 0 && _selectedNamedView is not null;
        _updating = false;
    }

    private async Task CommitRenameAsync()
    {
        if (_updating || _snapshot is null || _model?.RenameTarget is not { } target) return;
        var expected = _model.RenameValue;
        var next = _name.Text.Trim();
        if (next.Length == 0 || string.Equals(expected, next, StringComparison.Ordinal)) return;
        await RunAsync(_selectionSection, _selectionError,
            () => target.Kind == OverviewNodeKind.Sheet
                ? LayoutFoundryUiHost.RenameSheetAsync(target.Id, expected, next)
                : LayoutFoundryUiHost.RenameFolderAsync(target.Id, expected, next),
            $"Renamed to {next}.");
    }

    private async Task CommitNotesAsync()
    {
        if (_updating || _model is null || _model.EditableNotesTargets.Count == 0) return;
        if (!_model.NotesIsMixed && string.Equals(_model.NotesValue, _notes.Text, StringComparison.Ordinal))
            return;
        var count = _model.EditableNotesTargets.Count;
        await RunAsync(
            _selectionSection,
            _selectionError,
            () => LayoutFoundryUiHost.UpdateHierarchyNotesAsync(
                _model.EditableNotesTargets,
                _notes.Text),
            count == 1 ? "Notes updated." : $"Notes updated on {count} items.");
    }

    private async Task CommitPrintAsync()
    {
        if (_updating || _model is null || _print.Checked is not { } include) return;
        var targets = FrozenLayoutTargets();
        await RunAsync(_layoutSection, _layoutError,
            () => LayoutFoundryUiHost.SetPrintInclusionAsync(targets, include),
            "Print inclusion updated.");
    }

    private async Task CommitPaperAsync()
    {
        if (_updating || _paperCommitInProgress || _model is null ||
            _paperWidth.Value <= 0 || _paperHeight.Value <= 0 || _paperUnit.SelectedValue is not string unit)
            return;
        if (!_model.PaperIsMixed && _model.PaperWidth == _paperWidth.Value &&
            _model.PaperHeight == _paperHeight.Value &&
            string.Equals(_model.PaperUnitSystem, unit, StringComparison.OrdinalIgnoreCase)) return;
        _paperCommitInProgress = true;
        try
        {
            var targets = FrozenLayoutTargets();
            await RunAsync(_layoutSection, _layoutError,
                () => LayoutFoundryUiHost.SetPaperSizeAsync(
                    targets, _paperWidth.Value, _paperHeight.Value, unit),
                "Paper size updated.");
        }
        finally
        {
            _paperCommitInProgress = false;
        }
    }

    private async Task CommitTitleBlockAsync()
    {
        if (_snapshot is null || _model is null) return;
        var ids = _model.AffectedLayoutIds.ToArray();
        var builtIn = _titleBlockMode.SelectedIndex switch
        {
            1 => BuiltInTitleBlockKind.RightSidebar,
            2 => BuiltInTitleBlockKind.FullWidthBottom,
            _ => (BuiltInTitleBlockKind?)null,
        };
        await RunAsync(_layoutSection, _layoutError,
            () => LayoutFoundryUiHost.BatchUpdateSheetsAsync(new BatchUpdateSheetsRequest(
                DocumentRuntimeSerialNumber: _snapshot.DocumentRuntimeSerialNumber, SourceRevision: _snapshot.Revision, SheetPageViewIds: ids,
                NamingPattern: null, Start: 1, Step: 1, PaperWidth: null, PaperHeight: null, PaperUnitSystem: null, DetailDisplayModeId: null,
                ChangeTitleBlock: true,
                BuiltInTitleBlock: builtIn)),
            builtIn is null ? "Title blocks removed." : "Title-block mode updated.");
    }

    private async Task CommitTemplateRegistrationAsync()
    {
        var targets = _selection.Where(key => key.Kind is
OverviewNodeKind.Sheet or OverviewNodeKind.Detail).ToArray();
        if (_updating || targets.Length == 0) return;
        await RunAsync(_templateSection, _templateError,
            () => LayoutFoundryUiHost.SetLayoutTemplateRegistrationAsync(targets, _templateRegistration.Checked == true),
_templateRegistration.Checked != true ? "Template registration cleared." : "Template registration updated.");
    }

    private IEnumerable<HierarchyScope> SelectedScopes()
    {
        foreach (var key in _selection)
        {
            var kind = key.Kind switch
            {
                OverviewNodeKind.Folder => HierarchyScopeKind.Folder,
                OverviewNodeKind.Sheet => HierarchyScopeKind.Sheet,
                OverviewNodeKind.Detail => HierarchyScopeKind.Detail,
                _ => (HierarchyScopeKind?)null,
            };
            if (kind is { } value) yield return new HierarchyScope(value, key.Id);
        }
    }

    private async Task AssignStateAsync()
    {
        if (!_appearanceStateByLabel.TryGetValue(_appearanceState.Text.Trim(), out var stateId))
        {
            ShowError(_appearanceError, "Choose an appearance state.");
            return;
        }
        await RunAsync(_appearanceSection, _appearanceError,
            () => LayoutFoundryUiHost.AssignAppearanceStateAsync(_selection, stateId),
            "Appearance state assigned.");
    }

    private async Task ClearStateAssignmentAsync()
    {
        await RunAsync(_appearanceSection, _appearanceError,
            () => LayoutFoundryUiHost.AssignAppearanceStateAsync(_selection, null),
            "Appearance state returned to inherited basis. Local overrides were preserved.");
    }

    private async Task EditLocalAppearanceOverridesAsync()
    {
        if (_snapshot is null) return;
        var scopes = SelectedScopes().ToArray();
        if (scopes.Length == 0)
        {
            ShowError(_appearanceError, "Select a folder, layout, or detail first.");
            return;
        }

        var first = _snapshot.AppearanceRules.LastOrDefault(item => item.Scope == scopes[0]);
        var dialog = LocalAppearanceRulesDialog.ShowWithViewportPicking(
            ParentWindow,
            _snapshot,
            first?.LayerRules ?? [],
            first?.ObjectDisplayRules ?? []);
        if (!dialog.Accepted) return;
        await RunAsync(
            _appearanceSection,
            _appearanceError,
            () => LayoutFoundryUiHost.SetAppearanceRulesAsync(
                _selection,
                dialog.LayerRules,
                dialog.ObjectDisplayRules),
            scopes.Length == 1
                ? "Local appearance overrides updated."
                : $"Local appearance overrides updated on {scopes.Length} targets.");
    }

    private Dictionary<string, Guid> BuildStateChoices(
        DocumentSnapshot? snapshot)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        if (snapshot is null) return result;
        foreach (var state in snapshot.AppearanceStates
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var folder = snapshot.Folders.GetValueOrDefault(state.FolderId)?.Name ?? "Missing folder";
            var label = $"{state.Name} · {folder}";
            if (result.ContainsKey(label)) label += $" · {state.Id.ToString()[..8]}";
            result[label] = state.Id;
        }
        return result;
    }

    private string AssignedStateLabel(
        IReadOnlyList<HierarchyScope> scopes,
        IReadOnlyDictionary<string, Guid> states)
    {
        if (_snapshot is null || scopes.Count == 0) return string.Empty;
        var stateIds = scopes.Select(scope => _snapshot.StateAssignments.LastOrDefault(link =>
                link.Target == scope)?.StateId)
            .Distinct().ToArray();
        if (stateIds.Length != 1 || stateIds[0] is not { } id)
            return stateIds.Length > 1 ? Mixed : "Inherited";
        return states.FirstOrDefault(pair => pair.Value == id).Key ?? string.Empty;
    }

    private static string ScopeLabel(DocumentSnapshot snapshot, HierarchyScope scope)
    {
        var kind = scope.Kind switch
        {
            HierarchyScopeKind.Folder => "Folder",
            HierarchyScopeKind.Sheet => "Layout",
            HierarchyScopeKind.Detail => "Detail",
            _ => "Source",
        };
        var name = scope.Kind switch
        {
            HierarchyScopeKind.Folder => snapshot.Folders.GetValueOrDefault(scope.Id)?.Name,
            HierarchyScopeKind.Sheet => snapshot.Sheets.GetValueOrDefault(scope.Id)?.Name,
            HierarchyScopeKind.Detail => snapshot.Sheets.Values.SelectMany(sheet => sheet.Details)
                .FirstOrDefault(detail => detail.DetailViewportId == scope.Id)?.Name,
            _ => null,
        };
        return $"{kind}: {name ?? "Missing source"}";
    }

    private void ReloadLayers()
    {
        var snapshot = _snapshot;
        if (snapshot is null)
        {
            _layerRows = [];
            _layers.DataStore = _layerRows;
            return;
        }

        var scopes = SelectedScopes().ToArray();
        var localRules = scopes.Select(scope => snapshot.AppearanceRules
            .LastOrDefault(item => item.Scope == scope)?.LayerRules ?? []).ToArray();
        var query = _layerSearch.Text.Trim();
        _layerRows = snapshot.LayerSnapshots.Values
            .Where(layer => query.Length == 0 ||
                            layer.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(layer => layer.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(layer =>
            {
                var values = localRules.Select(rules => rules
                        .LastOrDefault(rule => rule.Layer.LayerId == layer.Id)?.Visibility)
                    .Distinct().ToArray();
                var state = values.Length != 1 ? Mixed : values[0] switch
                {
                    LayerVisibilityOverride.Visible => "On",
                    LayerVisibilityOverride.Hidden => "Off",
                    _ => "Inherit",
                };
                return new LayerRuleRow(layer.Id, layer.FullPath, state);
            })
            .ToArray();
        _layers.DataStore = _layerRows;
    }

    private async Task CommitLayerVisibilityAsync(LayerVisibilityOverride? visibility)
    {
        var indices = _layers.SelectedRows.OrderBy(index => index).ToArray();
        var layerIds = indices.Where(index => index >= 0 && index < _layerRows.Length)
            .Select(index => _layerRows[index].Id).Distinct().ToArray();
        if (layerIds.Length == 0)
        {
            ShowError(_layersError, "Select one or more layers first.");
            return;
        }
        await RunAsync(_layersSection, _layersError,
            () => LayoutFoundryUiHost.SetLayerVisibilityRulesAsync(_selection, layerIds, visibility),
            visibility switch
            {
                LayerVisibilityOverride.Visible => "Selected layers set on.",
                LayerVisibilityOverride.Hidden => "Selected layers set off.",
                _ => "Selected layers returned to inherited state.",
            });
    }

    private async Task ClearLayerOverridesAsync()
    {
        if (_snapshot is null) return;
        var scopes = SelectedScopes().ToHashSet();
        var layerIds = _snapshot.AppearanceRules
            .Where(item => scopes.Contains(item.Scope))
            .SelectMany(item => item.LayerRules)
            .Select(rule => rule.Layer.LayerId)
            .Distinct().ToArray();
        if (layerIds.Length == 0) return;
        await RunAsync(_layersSection, _layersError,
            () => LayoutFoundryUiHost.SetLayerVisibilityRulesAsync(_selection, layerIds, null),
            "Local layer overrides cleared.");
    }

    private void ReloadObjectRules()
    {
        if (_snapshot is null)
        {
            _objectRuleRows = [];
            _objectRules.DataStore = _objectRuleRows;
            return;
        }
        var ruleSets = SelectedScopes().Select(scope => _snapshot.AppearanceRules
            .LastOrDefault(item => item.Scope == scope)?.ObjectDisplayRules.ToArray() ?? []).ToArray();
        var mixed = ruleSets.Length > 1 && ruleSets.Skip(1).Any(rules => !rules.SequenceEqual(ruleSets[0]));
        ShowError(_objectsError, mixed
            ? "The selected items have different local object rules. Adding a rule will replace them with one shared set."
            : string.Empty);
        var rulesToShow = ruleSets.FirstOrDefault() ?? [];
        _objectRuleRows = rulesToShow.Select(rule => new ObjectRuleRow(
            rule,
            ObjectRuleTarget(rule),
            rule.DisplayModeName)).ToArray();
        _objectRules.DataStore = _objectRuleRows;
    }

    private IReadOnlyList<ObjectDisplayRule> CurrentObjectRules() =>
        _objectRuleRows.Select(row => row.Rule).ToArray();

    private async Task AddExactObjectRuleAsync()
    {
        if (!_objectIdByLabel.TryGetValue(_objectTarget.Text.Trim(), out var objectId))
        {
            ShowError(_objectsError, "Choose an exact model object.");
            return;
        }
        if (!TrySelectedObjectMode(out var modeId, out var modeName)) return;
        var selector = new ObjectDisplaySelector(ObjectDisplaySelectorKind.ExactObject, ObjectId: objectId);
        await SaveObjectRuleAsync(selector, modeId, modeName, "Object display rule added.");
    }

    private async Task AddLayerObjectRuleAsync()
    {
        if (_snapshot is null || !_layerIdByLabel.TryGetValue(_objectLayer.Text.Trim(), out var layerId) ||
            !_snapshot.LayerSnapshots.TryGetValue(layerId, out var layer))
        {
            ShowError(_objectsError, "Choose an object layer.");
            return;
        }
        if (!TrySelectedObjectMode(out var modeId, out var modeName)) return;
        var selector = new ObjectDisplaySelector(
            ObjectDisplaySelectorKind.Layer,
            LayerId: layerId,
            LayerFullPath: layer.FullPath);
        await SaveObjectRuleAsync(selector, modeId, modeName, "Layer display rule added.");
    }

    private bool TrySelectedObjectMode(out Guid modeId, out string modeName)
    {
        modeName = _objectMode.Text.Trim();
        if (_displayModeByLabel.TryGetValue(modeName, out modeId)) return true;
        ShowError(_objectsError, "Choose a Rhino display mode.");
        return false;
    }

    private async Task SaveObjectRuleAsync(
        ObjectDisplaySelector selector,
        Guid modeId,
        string modeName,
        string success)
    {
        var rules = CurrentObjectRules().Where(rule => rule.Selector != selector)
            .Append(new ObjectDisplayRule(selector, modeId, modeName)).ToArray();
        await RunAsync(_objectsSection, _objectsError,
            () => LayoutFoundryUiHost.SetObjectDisplayRulesAsync(_selection, rules), success);
    }

    private async Task RemoveObjectRulesAsync()
    {
        var selected = _objectRules.SelectedRows.ToHashSet();
        if (selected.Count == 0)
        {
            ShowError(_objectsError, "Select one or more object rules first.");
            return;
        }
        var rules = _objectRuleRows.Where((_, index) => !selected.Contains(index))
            .Select(row => row.Rule).ToArray();
        await RunAsync(_objectsSection, _objectsError,
            () => LayoutFoundryUiHost.SetObjectDisplayRulesAsync(_selection, rules),
            "Selected object rules removed.");
    }

    private string ObjectRuleTarget(ObjectDisplayRule rule)
    {
        if (rule.Selector.Kind == ObjectDisplaySelectorKind.ExactObject &&
            rule.Selector.ObjectId is { } objectId &&
            _snapshot?.ModelObjects.TryGetValue(objectId, out var modelObject) == true)
            return ObjectLabel(modelObject);
        if (rule.Selector.Kind == ObjectDisplaySelectorKind.Layer)
            return $"Layer: {rule.Selector.LayerFullPath ?? "Missing layer"} + children";
        return "Missing object";
    }

    private static string ObjectLabel(ModelObjectSnapshot item)
    {
        var name = string.IsNullOrWhiteSpace(item.Name) ? "Unnamed object" : item.Name;
        return $"{name} · {item.LayerFullPath} · {item.Id.ToString()[..8]}";
    }

    private async Task CommitRevisionsAsync()
    {
        if (_snapshot is null || _model is null) return;
        var revisions = ParseRevisions(_revisions.Text, out var error);
        if (error is not null)
        {
            ShowError(_layoutError, error);
            return;
        }
        var ids = _model.AffectedLayoutIds.ToArray();
        var single = ids.Length == 1;
        if (!single && revisions.Count != 1)
        {
            ShowError(_layoutError, "Enter exactly one revision to append to multiple layouts.");
            return;
        }
        await RunAsync(_layoutSection, _layoutError,
            () => LayoutFoundryUiHost.BatchUpdateSheetsAsync(new BatchUpdateSheetsRequest(
                DocumentRuntimeSerialNumber: _snapshot.DocumentRuntimeSerialNumber, SourceRevision: _snapshot.Revision, SheetPageViewIds: ids,
                NamingPattern: null, Start: 1, Step: 1, PaperWidth: null, PaperHeight: null, PaperUnitSystem: null, DetailDisplayModeId: null,
                ReplaceRevisionSchedule: single ? revisions : null,
                AppendRevision: single ? null : revisions[0])),
            single ? "Revision schedule saved." : "Revision appended.");
    }

    private async Task CommitDisplayModeAsync()
    {
        if (_model is null || !_displayModeByLabel.TryGetValue(_displayMode.Text.Trim(), out var modeId)) return;
        var targets = _model.AffectedDetailIds
            .Select(id => new OverviewNodeKey(OverviewNodeKind.Detail, id)).ToArray();
        await RunAsync(_detailSection, _detailError,
            () => LayoutFoundryUiHost.SetDisplayModeAsync(targets, modeId),
            "Display mode updated.");
    }

    private async Task CommitNamedViewAsync()
    {
        if (_model is null || string.IsNullOrWhiteSpace(_selectedNamedView)) return;
        var detailIds = _model.AffectedDetailIds.ToArray();
        var name = _selectedNamedView;
        await RunAsync(_namedViewSection, _namedViewError,
            () => LayoutFoundryUiHost.AssignNamedViewAsync(detailIds, name),
            $"Assigned {name} to {detailIds.Length} detail{(detailIds.Length == 1 ? string.Empty : "s")}.");
    }

    private async Task RunAsync(
        Control section,
        Label errorLabel,
        Func<Task<OperationResult>> operation,
        string success)
    {
        if (!_busySections.Add(section)) return;
        section.Enabled = false;
        ShowError(errorLabel, string.Empty);
        OperationResult result;
        try
        {
            result = await operation();
        }
        catch (Exception exception)
        {
            result = new OperationResult(false,
                [new RhinoLayoutFoundry.Core.Diagnostics.Diagnostic(
                    "inspector.operation_failed",
                    RhinoLayoutFoundry.Core.Diagnostics.DiagnosticSeverity.Error,
                    exception.Message)]);
        }
        finally
        {
            section.Enabled = true;
            _busySections.Remove(section);
        }

        if (!result.Succeeded)
        {
            var message = result.Diagnostics.FirstOrDefault()?.Message ?? "The operation could not be completed.";
            Populate();
            ShowError(errorLabel, message);
        }
        OperationCompleted?.Invoke(this, new SelectionInspectorOperationEventArgs(result, success));
    }

    private IReadOnlyList<OverviewNodeKey> FrozenLayoutTargets() => _model?.AffectedLayoutIds
        .Select(id => new OverviewNodeKey(OverviewNodeKind.Sheet, id)).ToArray() ?? [];

    private void SetNamedViewMode(bool thumbnails)
    {
        _namedViewListMode.Checked = !thumbnails;
        _namedViewThumbnailMode.Checked = thumbnails;
        _namedViewThumbnailSizeRow.Visible = thumbnails;
        _namedViews.Visible = !thumbnails;
        _namedViewThumbnailBrowser.Visible = thumbnails;
        ReloadNamedViews();
        if (thumbnails) NamedViewPreviewsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadNamedViews()
    {
        var names = _snapshot?.NamedViews.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var thumbnailMode = UsesNamedViewThumbnails;
        _namedViews.RowHeight = 28;
        _namedViews.Height = Math.Clamp(Math.Max(1, names.Length) * 28, 84, 224);
        if (_selectedNamedView is null || !names.Contains(_selectedNamedView, StringComparer.OrdinalIgnoreCase))
            _selectedNamedView = names.FirstOrDefault();
        _namedViewRows = names.Select(name => new NamedViewRow(
            name,
            null)).ToArray();
        _namedViews.DataStore = _namedViewRows;
        _namedViews.SelectedRow = Array.FindIndex(_namedViewRows,
            row => string.Equals(row.Name, _selectedNamedView, StringComparison.OrdinalIgnoreCase));
        _namedViewThumbnailGrid.SetItems(names.Select(name => new NamedViewThumbnailItem(
            name,
            _namedViewPreviews.TryGetValue(name, out var image) ? image : null)));
        _namedViewThumbnailGrid.SetSelectedName(_selectedNamedView);
        if (thumbnailMode) UpdateNamedViewThumbnailLayout();
    }

    private void UpdateNamedViewThumbnailLayout()
    {
        var availableWidth = _namedViewThumbnailBrowser.ClientSize.Width > 1
            ? _namedViewThumbnailBrowser.ClientSize.Width
            : OverlayWidth - FoundryTheme.Space3 * 2 - FoundryTheme.Space1;
        _namedViewThumbnailGrid.SetLayout(
            availableWidth,
            _namedViewThumbnailSize.Value,
            minimumHeight: 120);
        _namedViewThumbnailBrowser.Height = Math.Clamp(
            (int)Math.Ceiling(_namedViewThumbnailGrid.ContentHeight),
            120,
            352);
    }

    private void UpdateNamedViewThumbnailSizeLabel() =>
        _namedViewThumbnailSizeValue.Text = $"{_namedViewThumbnailSize.Value}px";

    private void WirePaperCommit(NumericStepper stepper)
    {
        stepper.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Enter) return;
            _ = CommitPaperAsync();
            eventArgs.Handled = true;
        };
        stepper.LostFocus += (_, _) => _ = CommitPaperAsync();
    }

    private static Panel Section(string title, Control content) => new()
    {
        BackgroundColor = FoundryTheme.CanvasOverlayBackground,
        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label
                {
                    Text = title,
                    Font = SystemFonts.Bold(11),
                    TextColor = FoundryTheme.PrimaryText,
                    TextAlignment = TextAlignment.Left,
                },
                content,
                new Panel { Height = 1, BackgroundColor = FoundryTheme.CanvasBorder },
            },
        },
    };

    private static StackLayout Field(string label, Control control) => new()
    {
        Spacing = FoundryTheme.Space1,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label
            {
                Text = label,
                Font = SystemFonts.Bold(9),
                TextColor = FoundryTheme.SecondaryText,
                TextAlignment = TextAlignment.Left,
            },
            control,
        },
    };

    private static Panel InspectorField(Control control, int minimumHeight = 32)
    {
        return new Panel
        {
            MinimumSize = new Size(0, minimumHeight),
            Content = new FoundryFormField(control, minimumHeight: minimumHeight),
        };
    }

    private static Label ErrorLabel() => new()
    {
        TextColor = FoundryTheme.DangerAccent,
        Wrap = WrapMode.Word,
        Visible = false,
    };

    private static void ShowError(Label label, string text)
    {
        label.Text = text;
        label.Visible = !string.IsNullOrWhiteSpace(text);
    }

    private void ClearErrors()
    {
        ShowError(_selectionError, string.Empty);
        ShowError(_templateError, string.Empty);
        ShowError(_layoutError, string.Empty);
        ShowError(_detailError, string.Empty);
        ShowError(_layersError, string.Empty);
        ShowError(_objectsError, string.Empty);
        ShowError(_namedViewError, string.Empty);
    }

    private static NumericStepper DimensionStepper() => new()
    {
        MinValue = 0,
        MaxValue = 100000,
        DecimalPlaces = 3,
        Increment = 1,
    };

    private static int FindPaperPreset(SelectionInspectorModel? model)
    {
        if (model is null || model.PaperIsMixed) return -1;
        var index = Array.FindIndex(PaperSizes, choice =>
            Math.Abs(choice.Width - model.PaperWidth.GetValueOrDefault()) < 0.001 &&
            Math.Abs(choice.Height - model.PaperHeight.GetValueOrDefault()) < 0.001 &&
            string.Equals(choice.UnitSystem, model.PaperUnitSystem, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : 0;
    }

    private static int TitleBlockModeIndex(SheetSnapshot sheet)
    {
        if (sheet.TitleBlockInstanceObjectId is null) return 0;
        return sheet.TitleBlockBuiltInKind switch
        {
            BuiltInTitleBlockKind.FullWidthBottom => 2,
            not null => 1,
            _ => -1,
        };
    }

    private static IReadOnlyList<SheetRevisionRecord> ParseRevisions(string text, out string? error)
    {
        var result = new List<SheetRevisionRecord>();
        foreach (var line in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var values = line.Split('|').Select(value => value.Trim()).ToArray();
            if (values.Length != 5)
            {
                error = "Each revision row needs five values separated by |.";
                return [];
            }
            result.Add(new SheetRevisionRecord(values[0], values[1], values[2], values[3], values[4]));
        }
        error = null;
        return result;
    }

    private static string FormatRevisions(IEnumerable<SheetRevisionRecord> revisions) => string.Join(
        Environment.NewLine,
        revisions.Select(revision =>
            $"{revision.Code} | {revision.Date} | {revision.Description} | {revision.IssuedBy} | {revision.CheckedBy}"));

    private static readonly PaperSizeChoice[] PaperSizes =
    [
        new("A0 P · 841 × 1189 mm", 841, 1189, "Millimeters"),
        new("A0 L · 1189 × 841 mm", 1189, 841, "Millimeters"),
        new("A1 P · 594 × 841 mm", 594, 841, "Millimeters"),
        new("A1 L · 841 × 594 mm", 841, 594, "Millimeters"),
        new("A2 P · 420 × 594 mm", 420, 594, "Millimeters"),
        new("A2 L · 594 × 420 mm", 594, 420, "Millimeters"),
        new("A3 P · 297 × 420 mm", 297, 420, "Millimeters"),
        new("A3 L · 420 × 297 mm", 420, 297, "Millimeters"),
        new("A4 P · 210 × 297 mm", 210, 297, "Millimeters"),
        new("A4 L · 297 × 210 mm", 297, 210, "Millimeters"),
        new("ANSI A P · 8.5 × 11 in", 8.5, 11, "Inches"),
        new("ANSI A L · 11 × 8.5 in", 11, 8.5, "Inches"),
        new("ANSI B P · 11 × 17 in", 11, 17, "Inches"),
        new("ANSI B L · 17 × 11 in", 17, 11, "Inches"),
        new("ANSI C P · 17 × 22 in", 17, 22, "Inches"),
        new("ANSI C L · 22 × 17 in", 22, 17, "Inches"),
        new("ANSI D P · 22 × 34 in", 22, 34, "Inches"),
        new("ANSI D L · 34 × 22 in", 34, 22, "Inches"),
    ];

    private sealed record PaperSizeChoice(string Label, double Width, double Height, string UnitSystem);
    private sealed record NamedViewRow(string Name, Image? Image);
    private sealed record LayerRuleRow(Guid Id, string Name, string State);
    private sealed record ObjectRuleRow(ObjectDisplayRule Rule, string Target, string Mode);
}

internal sealed class SelectionInspectorOperationEventArgs : EventArgs
{
    internal SelectionInspectorOperationEventArgs(OperationResult result, string successMessage)
    {
        Result = result;
        SuccessMessage = successMessage;
    }

    internal OperationResult Result { get; }
    internal string SuccessMessage { get; }
}
