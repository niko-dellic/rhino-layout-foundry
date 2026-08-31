using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class SelectionInspectorPanel : Panel
{
    internal const int OverlayWidth = 344;
    private const string Mixed = "Mixed";
    private const string CustomPaperPreset = "Custom";
    private const string RemoveTitleBlock = "Remove title block";
    private const int NamedViewThumbnailMinimum = 64;
    private const int NamedViewThumbnailMaximum = 240;
    private const int NamedViewThumbnailDefault = 128;
    private static readonly string[] Units = ["Millimeters", "Centimeters", "Meters", "Inches", "Feet"];

    private readonly Label _selectionSummary = FoundryTheme.MutedLabel();
    private readonly TextBox _name = new();
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
    private readonly FoundryCheckBox _template = new("Registered layout template");
    private readonly TextArea _revisions = new() { Height = 82, Wrap = false };
    private readonly FoundryDialogButton _revisionAction = new("Save", FoundryDialogButtonStyle.Secondary, 92);
    private readonly Label _layoutError = ErrorLabel();
    private readonly Panel _layoutSection;
    private readonly FilteredPicker _displayMode = new([], "Search display modes");
    private readonly Label _detailError = ErrorLabel();
    private readonly Panel _detailSection;
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
    private ObserverPoint _namedViewPress;
    private bool _updating;
    private bool _paperCommitInProgress;
    private string? _selectedNamedView;

    internal SelectionInspectorPanel()
    {
        Width = OverlayWidth;
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
                _selectionError,
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
                _template,
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

        _namedViewListMode = new FoundryToolbarIconButton(
            FoundryViewIcons.ListView(), "Show named views as a list", isToggle: true) { Checked = true };
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
            Items = { _selectionSection, _layoutSection, _detailSection, _namedViewSection },
        };
        Content = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            ExpandContentHeight = false,
            Content = content,
        };

        _name.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Enter) return;
            _ = CommitRenameAsync();
            eventArgs.Handled = true;
        };
        _name.LostFocus += (_, _) => _ = CommitRenameAsync();
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
        _template.CheckedChanged += (_, _) =>
        {
            if (!_updating && _template.Checked is not null) _ = CommitTemplateAsync();
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
            ? "A legacy custom title block is present. Choosing a mode replaces it."
            : titleBlockModes.Length > 1 ? "The selected layouts use mixed title-block modes." : null;
        _template.Visible = model?.TemplateRegistered is not null;
        _template.Checked = model?.TemplateRegistered;
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
                _snapshot.DocumentRuntimeSerialNumber, _snapshot.Revision, ids,
                null, 1, 1, null, null, null, null,
                ChangeTitleBlock: true,
                BuiltInTitleBlock: builtIn)),
            builtIn is null ? "Title blocks removed." : "Title-block mode updated.");
    }

    private async Task CommitTemplateAsync()
    {
        if (_model?.TemplateRegistered is null || _model.AffectedLayoutIds.Count != 1 ||
            _template.Checked is not { } registered) return;
        var sheetId = _model.AffectedLayoutIds[0];
        await RunAsync(_layoutSection, _layoutError,
            () => LayoutFoundryUiHost.SetSheetTemplateRegistrationAsync(sheetId, registered),
            registered ? "Layout template registered." : "Layout template unregistered.");
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
                _snapshot.DocumentRuntimeSerialNumber, _snapshot.Revision, ids,
                null, 1, 1, null, null, null, null,
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
        ShowError(_layoutError, string.Empty);
        ShowError(_detailError, string.Empty);
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

    private static Dictionary<string, Guid?> BuildTitleBlockChoices(DocumentSnapshot? snapshot)
    {
        var result = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase)
        {
            [RemoveTitleBlock] = null,
        };
        if (snapshot is null) return result;
        foreach (var block in snapshot.TitleBlockInstances.Values
                     .OrderBy(block => block.InstanceDefinitionName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(block => block.SourcePageName, StringComparer.OrdinalIgnoreCase))
        {
            var label = $"{block.InstanceDefinitionName} · {block.SourcePageName}";
            if (result.ContainsKey(label)) label += $" · {block.InstanceObjectId.ToString()[..8]}";
            result[label] = block.InstanceObjectId;
        }
        return result;
    }

    private static string TitleBlockText(SelectionInspectorModel? model, DocumentSnapshot? snapshot)
    {
        if (model?.TitleBlockIsMixed == true) return Mixed;
        if (model?.TitleBlockSourceInstanceId is not { } id) return RemoveTitleBlock;
        var block = snapshot?.TitleBlockInstances.GetValueOrDefault(id);
        return block is null ? string.Empty : $"{block.InstanceDefinitionName} · {block.SourcePageName}";
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
