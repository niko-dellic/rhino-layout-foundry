using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class BatchCreateLayoutsDialog : Dialog
{
    private const string InheritDisplayMode = "Use layout/template setting";
    private const string InheritNamedView = "Use detail/template camera";
    private readonly DocumentSnapshot _snapshot;
    private readonly IReadOnlyList<(Guid Id, string Label)> _folders;
    private readonly LayoutChoice[] _layoutChoices;
    private readonly TitleBlockChoice[] _titleBlockChoices;
    private readonly NamedViewChoice[] _namedViewChoices;
    private readonly DropDown _destinationDropDown;
    private readonly NumericStepper _quantityStepper;
    private readonly TextBox _patternBox;
    private readonly NumericStepper _startStepper;
    private readonly NumericStepper _stepStepper;
    private readonly LayoutPreviewTray _layoutPreviewTray;
    private readonly LayoutSelectionDrawable _layoutSelectorPreview;
    private readonly DropDown _paperPresetDropDown;
    private readonly DropDown _orientationDropDown;
    private readonly NumericStepper _widthStepper;
    private readonly NumericStepper _heightStepper;
    private readonly DropDown _unitDropDown;
    private readonly FilteredPicker _displayModePicker;
    private readonly FoundryCheckBox _dedicatedDetailLayerCheck;
    private readonly TitleBlockPreviewTray _titleBlockPreviewTray;
    private readonly TitleBlockSelectionDrawable _titleBlockSelectorPreview;
    private readonly NamedViewPreviewTray _namedViewPreviewTray;
    private readonly Panel _detailViewAssignmentsHost;
    private readonly StackLayout _layoutGroupChips;
    private readonly Scrollable _layoutGroupChipScroll;
    private readonly Label _namedViewGalleryPrompt;
    private readonly GridView _previewGrid;
    private readonly Label _countLabel;
    private readonly Label _selectionHint;
    private readonly FoundryToolbarIconButton _clearSelectionButton;
    private readonly Label _status;
    private readonly FoundryDialogButton _createButton;
    private readonly List<CreationDraft> _drafts = [];
    private readonly List<CreationPreviewRow> _visiblePreviewRows = [];
    private readonly List<NamedViewSelectionDrawable> _detailViewSelectors = [];
    private LayoutGroupKey? _activeGroupFilter;
    private int? _activeDetailIndex;
    private NamedViewSelectionDrawable? _activeNamedViewSelector;
    private Form? _layoutGallery;
    private Scrollable? _layoutGalleryScroll;
    private Form? _titleBlockGallery;
    private Scrollable? _titleBlockGalleryScroll;
    private Form? _namedViewGallery;
    private Scrollable? _namedViewGalleryScroll;
    private readonly CancellationTokenSource _namedViewPreviewCancellation = new();
    private bool _namedViewPreviewLoadStarted;
    private bool _updatingPaper;
    private bool _updatingEditors;
    private bool _updatingPreviewSelection;

    internal BatchCreateLayoutsDialog(DocumentSnapshot snapshot, Guid? preferredFolderId)
    {
        _snapshot = snapshot;
        _folders = FolderChoices(snapshot);
        _layoutChoices = LayoutChoices(snapshot);
        _titleBlockChoices = TitleBlockChoices(snapshot);
        _namedViewChoices = NamedViewChoices(snapshot);
        Title = "Create layouts";
        MinimumSize = new Size(1360, 760);
        Resizable = true;
        Padding = new Padding(FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _destinationDropDown = new DropDown { DataStore = _folders.Select(item => item.Label).ToArray() };
        _destinationDropDown.SelectedIndex = PreferredFolderIndex(preferredFolderId);
        _quantityStepper = IntegerStepper(1, 1, 999);
        _patternBox = new TextBox { Text = "Page {index}" };
        _startStepper = IntegerStepper(FirstAvailablePageNumber(snapshot), -999999, 999999);
        _stepStepper = IntegerStepper(1, -999999, 999999);
        _paperPresetDropDown = new DropDown
        {
            DataStore = PaperPresets.Select(preset => preset.Label).ToArray(),
            SelectedIndex = 3,
        };
        _orientationDropDown = new DropDown
        {
            DataStore = new[] { "Landscape", "Portrait" },
            SelectedIndex = 0,
        };
        _widthStepper = DimensionStepper(594);
        _heightStepper = DimensionStepper(420);
        _unitDropDown = new DropDown
        {
            DataStore = Units,
            SelectedIndex = 0,
        };
        _layoutPreviewTray = new LayoutPreviewTray(_layoutChoices, selectedIndex: 1);
        _layoutSelectorPreview = new LayoutSelectionDrawable(_layoutChoices, selectedIndex: 1);
        _layoutSelectorPreview.ToolTip = "Open the layout gallery";
        UpdateLayoutSelector();
        _displayModePicker = new FilteredPicker(
            new[] { InheritDisplayMode }.Concat(snapshot.DisplayModes.Values),
            "Search display modes");
        _displayModePicker.Text = InheritDisplayMode;
        _dedicatedDetailLayerCheck = new FoundryCheckBox(
            "Place details on dedicated .details layer",
            isChecked: true)
        {
            ToolTip = "Foundry tracks this layer by identity, so it can be renamed or moved in the layer hierarchy.",
        };
        _titleBlockPreviewTray = new TitleBlockPreviewTray(_titleBlockChoices, selectedIndex: 0);
        _titleBlockSelectorPreview = new TitleBlockSelectionDrawable(_titleBlockChoices, selectedIndex: 0);
        _namedViewPreviewTray = new NamedViewPreviewTray(_namedViewChoices, selectedIndex: 0);
        _detailViewAssignmentsHost = new Panel();
        _layoutGroupChips = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space1,
        };
        _layoutGroupChipScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = false,
            ExpandContentHeight = false,
            Height = 36,
            Content = _layoutGroupChips,
        };
        _namedViewGalleryPrompt = new Label
        {
            Text = "Choose a named view for this detail.",
            Font = SystemFonts.Bold(10),
            TextColor = FoundryTheme.PrimaryText,
        };
        _previewGrid = CreatePreviewGrid();
        _countLabel = new Label { Font = SystemFonts.Bold(13), TextColor = FoundryTheme.PrimaryText };
        _selectionHint = FoundryTheme.MutedLabel();
        _clearSelectionButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ClearSelection(),
            "Clear row selection and edit all layouts");
        _clearSelectionButton.Enabled = false;
        _clearSelectionButton.Click += (_, _) => ClearPreviewSelection();
        _status = FoundryTheme.MutedLabel();
        _status.Wrap = WrapMode.Word;
        _status.Visible = false;
        _createButton = new FoundryDialogButton(
            "Create layouts",
            FoundryDialogButtonStyle.Primary,
            118);
        var cancel = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        cancel.Click += (_, _) => Close();
        _createButton.Click += async (_, _) => await CreateAsync();
        FoundryDialogActions.Bind(this, _createButton, cancel);

        _drafts.Add(DraftFromEditors());

        _destinationDropDown.SelectedIndexChanged += (_, _) => QueueRefreshPreview();
        _quantityStepper.ValueChanged += (_, _) => ResizeDrafts();
        _startStepper.ValueChanged += (_, _) => RefreshPreview();
        _stepStepper.ValueChanged += (_, _) => RefreshPreview();
        _widthStepper.ValueChanged += (_, _) => ApplyPaperToTargets();
        _heightStepper.ValueChanged += (_, _) => ApplyPaperToTargets();
        _unitDropDown.SelectedIndexChanged += (_, _) => ApplyPaperToTargets();
        _patternBox.TextChanged += (_, _) => RefreshPreview();
        _paperPresetDropDown.SelectedIndexChanged += (_, _) => QueueApplyPaperPreset();
        _orientationDropDown.SelectedIndexChanged += (_, _) => QueueApplyPaperPreset();
        _layoutSelectorPreview.Activated += (_, _) => ToggleLayoutGallery();
        _layoutPreviewTray.SelectedIndexChanged += OnLayoutSelectionChanged;
        _layoutPreviewTray.SelectionCommitted += (_, _) => HideLayoutGallery();
        _displayModePicker.ValueChanged += (_, _) => ApplyDisplayModeToTargets();
        _dedicatedDetailLayerCheck.CheckedChanged += (_, _) => ApplyDedicatedDetailLayerToTargets();
        _titleBlockSelectorPreview.Activated += (_, _) => ToggleTitleBlockGallery();
        _titleBlockPreviewTray.SelectedIndexChanged += OnTitleBlockSelectionChanged;
        _titleBlockPreviewTray.SelectionCommitted += (_, _) => HideTitleBlockGallery();
        _namedViewPreviewTray.SelectedIndexChanged += OnNamedViewSelectionChanged;
        _namedViewPreviewTray.SelectionCommitted += (_, _) => HideNamedViewGallery();
        _previewGrid.SelectedRowsChanged += OnPreviewSelectionChanged;
        _displayModePicker.Opened += (_, _) =>
        {
            HideLayoutGallery();
            HideTitleBlockGallery();
            HideNamedViewGallery();
        };
        Closed += (_, _) =>
        {
            _namedViewPreviewCancellation.Cancel();
            CloseLayoutGallery();
            CloseTitleBlockGallery();
            CloseNamedViewGallery();
            _namedViewPreviewTray.DisposePreviews();
            _namedViewPreviewCancellation.Dispose();
        };
        LocationChanged += (_, _) =>
        {
            PositionLayoutGallery();
            PositionTitleBlockGallery();
            PositionNamedViewGallery();
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Header(),
                new StackLayoutItem(CreateLayoutsTab(), true),
                _status,
                new TableLayout
                {
                    Rows = { new TableRow(new TableCell(null, true), cancel, _createButton) },
                    Spacing = new Size(FoundryTheme.Space2, 0),
                },
            },
        };
        RefreshPreview();
    }

    internal int CreatedCount { get; private set; }
    internal bool Succeeded { get; private set; }

    private Control CreateLayoutsTab() => new StackLayout
    {
        Padding = new Padding(0, FoundryTheme.Space3, 0, 0),
        Spacing = FoundryTheme.Space3,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space3, 0),
                Rows =
                {
                    new TableRow(
                        new TableCell(Card("Batch", CreateBatchEditor())),
                        new TableCell(Card("Page size", CreatePaperEditor())),
                        new TableCell(Card("Details", CreateDetailEditor())),
                        new TableCell(Card("Title block", CreateTitleBlockEditor())),
                        new TableCell(Card("Layout && views", CreateLayoutEditor()), true)),
                },
            },
            _layoutGroupChipScroll,
            new StackLayout
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Center,
                Spacing = FoundryTheme.Space2,
                Items =
                {
                    _countLabel,
                    _selectionHint,
                    new StackLayoutItem(null, true),
                    _clearSelectionButton,
                },
            },
            new StackLayoutItem(_previewGrid, true),
        },
    };

    private Control CreateBatchEditor() => new TableLayout
    {
        Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2),
        Rows =
        {
            new TableRow(new Label { Text = "Destination" }, new TableCell(new FoundryFormField(_destinationDropDown), true)),
            new TableRow(new Label { Text = "Quantity" }, new FoundryFormField(_quantityStepper)),
            new TableRow(new Label { Text = "Name / pattern" }, new TableCell(new FoundryFormField(_patternBox), true)),
            new TableRow(new Label { Text = "Start / step" }, new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space1,
                Items = { new FoundryFormField(_startStepper), new FoundryFormField(_stepStepper) },
            }),
        },
    };

    private Control CreateLayoutEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            _layoutSelectorPreview,
            new Label
            {
                Text = "Assign a named view independently to each detail.",
                TextColor = FoundryTheme.MutedText,
                Wrap = WrapMode.Word,
            },
            _detailViewAssignmentsHost,
        },
    };

    private Control CreatePaperEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new FoundryFormField(_paperPresetDropDown),
            new FoundryFormField(_orientationDropDown),
            new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
                Rows =
                {
                    new TableRow(new Label { Text = "Width" }, new FoundryFormField(_widthStepper)),
                    new TableRow(new Label { Text = "Height" }, new FoundryFormField(_heightStepper)),
                    new TableRow(new Label { Text = "Units" }, new FoundryFormField(_unitDropDown)),
                },
            },
        },
    };

    private Control CreateDetailEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Apply one Rhino display mode to every created detail." },
            _displayModePicker,
            _dedicatedDetailLayerCheck,
            new Label
            {
                Text = "The layer can be renamed or moved later; Foundry will continue using the same layer.",
                TextColor = FoundryTheme.MutedText,
                Wrap = WrapMode.Word,
            },
        },
    };

    private Control CreateTitleBlockEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            _titleBlockSelectorPreview,
        },
    };

    private GridView CreatePreviewGrid()
    {
        var grid = new GridView
        {
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            Height = 270,
            ToolTip = "Select one or more rows to edit only those layouts. Clear the selection to edit all layouts.",
        };
        grid.Columns.Add(TextColumn("#", row => row.Index, 44));
        grid.Columns.Add(TextColumn("Layout name", row => row.Name, 190, true));
        grid.Columns.Add(TextColumn("Layout type", row => row.LayoutType, 190, true));
        grid.Columns.Add(TextColumn("Paper", row => row.Paper, 170));
        grid.Columns.Add(TextColumn("Details", row => row.Details, 70));
        grid.Columns.Add(TextColumn("Detail layer", row => row.DetailLayer, 105));
        grid.Columns.Add(TextColumn("Display mode", row => row.DisplayMode, 150, true));
        grid.Columns.Add(TextColumn("Title block", row => row.TitleBlock, 160, true));
        grid.CellFormatting += (_, eventArgs) =>
        {
            if (grid.SelectedRows.Contains(eventArgs.Row))
            {
                eventArgs.BackgroundColor = SystemColors.Selection;
                eventArgs.ForegroundColor = SystemColors.SelectionText;
                return;
            }

            eventArgs.BackgroundColor = eventArgs.Row % 2 == 0
                ? FoundryTheme.ContentBackground
                : FoundryTheme.HierarchyFolderBackground;
            eventArgs.ForegroundColor = FoundryTheme.PrimaryText;
        };
        return grid;
    }

    private BatchCreateSheetsRequest Request()
    {
        return new BatchCreateSheetsRequest(
            _snapshot.DocumentRuntimeSerialNumber,
            _snapshot.Revision,
            _folders[Math.Max(0, _destinationDropDown.SelectedIndex)].Id,
            [],
            _patternBox.Text,
            (int)_startStepper.Value,
            (int)_stepStepper.Value,
            CreationSpecs: _drafts.Select(draft => draft.ToSpec()).ToArray(),
            ProjectData: _snapshot.ProjectInfo);
    }

    private void RefreshPreview(bool refreshDetailAssignments = true)
    {
        if (_updatingPaper || _folders.Count == 0) return;
        var selectedDraftIds = SelectedDraftIds().ToHashSet();
        var plan = new BatchCreateSheetsPlanner().Plan(Request(), _snapshot);
        var changes = plan.Changes.OfType<CreateSheetFromTemplateChange>().ToArray();
        var allRows = changes.Select((change, index) => new CreationPreviewRow(
            _drafts[index].DraftId,
            LayoutGroupKey.For(_drafts[index].Layout),
            (index + 1).ToString(),
            change.Name,
            change.Template.Name,
            $"{change.Template.Paper.Width:0.###} × {change.Template.Paper.Height:0.###} {change.Template.Paper.UnitSystem}",
            change.Template.DetailSlots.Count.ToString(),
            change.Template.DetailSlots.Count == 0
                ? "—"
                : change.UseDedicatedDetailLayer ? ".details" : "Active layer",
            DisplayModeSummary(change.Template),
            change.Template.TitleBlock?.InstanceDefinitionName ?? "None")).ToArray();
        EnsureActiveGroupExists();
        _visiblePreviewRows.Clear();
        _visiblePreviewRows.AddRange(allRows.Where(row =>
            _activeGroupFilter is null || row.GroupKey == _activeGroupFilter));
        _updatingPreviewSelection = true;
        try
        {
            _previewGrid.DataStore = _visiblePreviewRows.ToArray();
            _previewGrid.SelectedRows = _visiblePreviewRows
                .Select((row, index) => (row, index))
                .Where(item => selectedDraftIds.Contains(item.row.DraftId))
                .Select(item => item.index)
                .ToArray();
        }
        finally
        {
            _updatingPreviewSelection = false;
        }
        CreatedCount = changes.Length;
        _countLabel.Text = $"Layouts to create  ·  {CreatedCount}";
        RefreshGroupChips();
        UpdateSelectionHint();
        if (refreshDetailAssignments) RefreshDetailAssignments();
        var pickerError = PickerError();
        var diagnostics = string.Join(" ", plan.Diagnostics
            .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(item => item.Code != "batch.undo_unavailable")
            .Select(item => item.Message));
        SetStatus(pickerError ?? diagnostics);
        _createButton.Text = CreatedCount == 1 ? "Create layout" : $"Create {CreatedCount} layouts";
        _createButton.Enabled = plan.CanApply && pickerError is null;
    }

    private void QueueRefreshPreview()
    {
        Application.Instance.AsyncInvoke(() => RefreshPreview());
    }

    private string? PickerError()
    {
        if (!string.Equals(_displayModePicker.Text.Trim(), InheritDisplayMode, StringComparison.OrdinalIgnoreCase) &&
            !_snapshot.DisplayModes.Values.Any(name => string.Equals(
                name, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Choose an available Rhino display mode or use the layout/template setting.";
        return null;
    }

    private async Task CreateAsync()
    {
        try
        {
            _createButton.Enabled = false;
            _createButton.Text = "Creating…";
            SetStatus($"Creating {CreatedCount} layout{(CreatedCount == 1 ? string.Empty : "s")}…");
            var result = await LayoutFoundryUiHost.BatchCreateSheetsAsync(Request());
            if (!result.Succeeded)
            {
                RefreshPreview();
                var message = string.Join(" ", result.Diagnostics.Select(item => item.Message));
                SetStatus(string.IsNullOrWhiteSpace(message)
                    ? "Rhino did not create the layouts. Review the settings and try again."
                    : message);
                MessageBox.Show(this, _status.Text, "Create layouts", MessageBoxType.Error);
                return;
            }
            Succeeded = true;
            Close();
        }
        catch (Exception exception)
        {
            RefreshPreview();
            SetStatus($"Layout creation failed: {exception.Message}");
            MessageBox.Show(this, _status.Text, "Create layouts", MessageBoxType.Error);
        }
    }

    private void SetStatus(string? message)
    {
        _status.Text = message ?? string.Empty;
        _status.Visible = !string.IsNullOrWhiteSpace(_status.Text);
    }

    private void ResizeDrafts()
    {
        if (_updatingEditors) return;
        var quantity = (int)_quantityStepper.Value;
        while (_drafts.Count < quantity)
            _drafts.Add(DraftFromEditors());
        if (_drafts.Count > quantity)
            _drafts.RemoveRange(quantity, _drafts.Count - quantity);
        RefreshPreview();
    }

    private CreationDraft DraftFromEditors()
    {
        var displayModeId = _snapshot.DisplayModes.FirstOrDefault(pair => string.Equals(
            pair.Value, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Key;
        var titleBlock = _titleBlockChoices[Math.Max(0, _titleBlockPreviewTray.SelectedIndex)];
        return new CreationDraft(
            Guid.NewGuid(),
            _layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)],
            CurrentPaper(),
            displayModeId == Guid.Empty ? null : displayModeId,
            _dedicatedDetailLayerCheck.Checked == true,
            titleBlock,
            DefaultNamedViews(_layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)]));
    }

    private void ApplyLayoutToTargets()
    {
        if (_updatingEditors) return;
        var layout = _layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)];
        var selected = SelectedDraftIds().ToHashSet();
        foreach (var index in TargetDraftIndices())
            _drafts[index] = _drafts[index] with
            {
                Layout = layout,
                NamedViewsByDetail = DefaultNamedViews(layout),
            };
        _activeGroupFilter = LayoutGroupKey.For(layout);
        RefreshPreview();
        if (selected.Count > 0) SelectDrafts(selected);
    }

    private void OnLayoutSelectionChanged(object? sender, EventArgs eventArgs)
    {
        UpdateLayoutSelector();
        ApplyLayoutToTargets();
    }

    private void UpdateLayoutSelector()
    {
        _layoutSelectorPreview.SetSelection(
            Math.Max(0, _layoutPreviewTray.SelectedIndex),
            _layoutGallery?.Visible == true);
    }

    private void ToggleLayoutGallery()
    {
        if (_layoutGallery?.Visible == true)
        {
            HideLayoutGallery();
            return;
        }

        _displayModePicker.CloseResults();
        HideTitleBlockGallery();
        HideNamedViewGallery();
        var gallery = EnsureLayoutGallery();
        PositionLayoutGallery();
        gallery.Show();
        gallery.BringToFront();
        UpdateLayoutSelector();
        Application.Instance.AsyncInvoke(() =>
        {
            ScrollLayoutSelectionIntoView();
            _layoutPreviewTray.Focus();
        });
    }

    private Form EnsureLayoutGallery()
    {
        if (_layoutGallery is not null) return _layoutGallery;

        var scrollable = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = false,
            ExpandContentHeight = false,
            Content = _layoutPreviewTray,
        };
        _layoutGalleryScroll = scrollable;
        var gallery = new Form
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Resizable = false,
            Maximizable = false,
            Minimizable = false,
            Closeable = false,
            AutoSize = false,
            BackgroundColor = FoundryTheme.CanvasBorder,
            Padding = new Padding(1),
            Content = new Panel
            {
                BackgroundColor = FoundryTheme.CanvasSurface,
                Padding = new Padding(FoundryTheme.Space2),
                Content = new StackLayout
                {
                    Spacing = FoundryTheme.Space2,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new Label
                        {
                            Text = "Choose a built-in arrangement or a captured layout template.",
                            Font = SystemFonts.Bold(10),
                            TextColor = FoundryTheme.PrimaryText,
                        },
                        new StackLayoutItem(scrollable, true),
                    },
                },
            },
        };
        gallery.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            HideLayoutGallery();
            eventArgs.Handled = true;
        };
        gallery.LostFocus += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (_layoutGallery != gallery || !gallery.Visible) return;
            var mouse = Mouse.Position;
            var mousePoint = new Point((int)Math.Round(mouse.X), (int)Math.Round(mouse.Y));
            if (gallery.Bounds.Contains(mousePoint) || _layoutPreviewTray.HasFocus) return;
            HideLayoutGallery();
        });
        gallery.Closed += (_, _) =>
        {
            if (ReferenceEquals(_layoutGallery, gallery))
            {
                _layoutGallery = null;
                _layoutGalleryScroll = null;
            }
            UpdateLayoutSelector();
        };
        _layoutGallery = gallery;
        PositionLayoutGallery();
        return gallery;
    }

    private void PositionLayoutGallery()
    {
        if (_layoutGallery is null) return;
        var selectorBottomLeft = _layoutSelectorPreview.PointToScreen(
            new PointF(0, _layoutSelectorPreview.Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(selectorBottomLeft)) ??
                     Screen.PrimaryScreen;
        var workArea = screen.WorkingArea;
        var workLeft = (int)Math.Ceiling(workArea.Left);
        var workTop = (int)Math.Ceiling(workArea.Top);
        var workRight = (int)Math.Floor(workArea.Right);
        var workBottom = (int)Math.Floor(workArea.Bottom);
        var desiredWidth = Math.Clamp(_layoutPreviewTray.ContentWidth + FoundryTheme.Space4 + 2, 440, 780);
        var width = Math.Min(desiredWidth, Math.Max(320, workRight - workLeft - FoundryTheme.Space4 * 2));
        var height = LayoutPreviewTray.TrayHeight + 42 + FoundryTheme.Space4 + 2;
        var x = (int)Math.Round(selectorBottomLeft.X + _layoutSelectorPreview.Width - width);
        x = Math.Clamp(x, workLeft + FoundryTheme.Space2, workRight - width - FoundryTheme.Space2);
        var y = (int)Math.Round(selectorBottomLeft.Y + FoundryTheme.Space1);
        if (y + height > workBottom - FoundryTheme.Space2)
        {
            var selectorTop = _layoutSelectorPreview.PointToScreen(PointF.Empty).Y;
            y = (int)Math.Round(selectorTop - height - FoundryTheme.Space1);
        }
        y = Math.Clamp(y, workTop + FoundryTheme.Space2, workBottom - height - FoundryTheme.Space2);
        _layoutGallery.Size = new Size(width, height);
        _layoutGallery.Location = new Point(x, y);
    }

    private void HideLayoutGallery()
    {
        if (_layoutGallery is not null) _layoutGallery.Visible = false;
        UpdateLayoutSelector();
    }

    private void CloseLayoutGallery()
    {
        if (_layoutGallery is null) return;
        var gallery = _layoutGallery;
        _layoutGallery = null;
        _layoutGalleryScroll = null;
        gallery.Close();
    }

    private void ScrollLayoutSelectionIntoView()
    {
        if (_layoutGalleryScroll is null || _layoutGallery is null) return;
        var viewportWidth = Math.Max(1, _layoutGallery.Width - FoundryTheme.Space4 - 2);
        var maximum = Math.Max(0, _layoutPreviewTray.ContentWidth - viewportWidth);
        var target = Math.Clamp(_layoutPreviewTray.SelectedCenter - viewportWidth / 2, 0, maximum);
        _layoutGalleryScroll.ScrollPosition = new Point(target, 0);
    }

    private void ToggleTitleBlockGallery()
    {
        if (_titleBlockGallery?.Visible == true)
        {
            HideTitleBlockGallery();
            return;
        }

        _displayModePicker.CloseResults();
        HideLayoutGallery();
        HideNamedViewGallery();
        var gallery = EnsureTitleBlockGallery();
        PositionTitleBlockGallery();
        gallery.Show();
        gallery.BringToFront();
        UpdateTitleBlockSelector();
        Application.Instance.AsyncInvoke(() =>
        {
            ScrollTitleBlockSelectionIntoView();
            _titleBlockPreviewTray.Focus();
        });
    }

    private Form EnsureTitleBlockGallery()
    {
        if (_titleBlockGallery is not null) return _titleBlockGallery;
        var scrollable = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = false,
            ExpandContentHeight = false,
            Content = _titleBlockPreviewTray,
        };
        _titleBlockGalleryScroll = scrollable;
        var gallery = new Form
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Resizable = false,
            Maximizable = false,
            Minimizable = false,
            Closeable = false,
            AutoSize = false,
            BackgroundColor = FoundryTheme.CanvasBorder,
            Padding = new Padding(1),
            Content = new Panel
            {
                BackgroundColor = FoundryTheme.CanvasSurface,
                Padding = new Padding(FoundryTheme.Space2),
                Content = new StackLayout
                {
                    Spacing = FoundryTheme.Space2,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new Label
                        {
                            Text = "Use the template, no block, or copy a page-space block instance.",
                            Font = SystemFonts.Bold(10),
                            TextColor = FoundryTheme.PrimaryText,
                        },
                        new StackLayoutItem(scrollable, true),
                    },
                },
            },
        };
        gallery.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            HideTitleBlockGallery();
            eventArgs.Handled = true;
        };
        gallery.LostFocus += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (_titleBlockGallery != gallery || !gallery.Visible) return;
            var mouse = Mouse.Position;
            var mousePoint = new Point((int)Math.Round(mouse.X), (int)Math.Round(mouse.Y));
            if (gallery.Bounds.Contains(mousePoint) || _titleBlockPreviewTray.HasFocus) return;
            HideTitleBlockGallery();
        });
        gallery.Closed += (_, _) =>
        {
            if (ReferenceEquals(_titleBlockGallery, gallery))
            {
                _titleBlockGallery = null;
                _titleBlockGalleryScroll = null;
            }
            UpdateTitleBlockSelector();
        };
        _titleBlockGallery = gallery;
        PositionTitleBlockGallery();
        return gallery;
    }

    private void PositionTitleBlockGallery()
    {
        if (_titleBlockGallery is null) return;
        var anchor = _titleBlockSelectorPreview.PointToScreen(
            new PointF(0, _titleBlockSelectorPreview.Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(anchor)) ??
                     Screen.PrimaryScreen;
        var work = screen.WorkingArea;
        var left = (int)Math.Ceiling(work.Left);
        var top = (int)Math.Ceiling(work.Top);
        var right = (int)Math.Floor(work.Right);
        var bottom = (int)Math.Floor(work.Bottom);
        var desiredWidth = Math.Clamp(_titleBlockPreviewTray.ContentWidth + FoundryTheme.Space4 + 2, 440, 780);
        var width = Math.Min(desiredWidth, Math.Max(320, right - left - FoundryTheme.Space4 * 2));
        var height = TitleBlockPreviewTray.TrayHeight + 42 + FoundryTheme.Space4 + 2;
        var x = (int)Math.Round(anchor.X + _titleBlockSelectorPreview.Width - width);
        x = Math.Clamp(x, left + FoundryTheme.Space2, right - width - FoundryTheme.Space2);
        var y = (int)Math.Round(anchor.Y + FoundryTheme.Space1);
        if (y + height > bottom - FoundryTheme.Space2)
        {
            var selectorTop = _titleBlockSelectorPreview.PointToScreen(PointF.Empty).Y;
            y = (int)Math.Round(selectorTop - height - FoundryTheme.Space1);
        }
        y = Math.Clamp(y, top + FoundryTheme.Space2, bottom - height - FoundryTheme.Space2);
        _titleBlockGallery.Size = new Size(width, height);
        _titleBlockGallery.Location = new Point(x, y);
    }

    private void HideTitleBlockGallery()
    {
        if (_titleBlockGallery is not null) _titleBlockGallery.Visible = false;
        UpdateTitleBlockSelector();
    }

    private void CloseTitleBlockGallery()
    {
        if (_titleBlockGallery is null) return;
        var gallery = _titleBlockGallery;
        _titleBlockGallery = null;
        _titleBlockGalleryScroll = null;
        gallery.Close();
    }

    private void ScrollTitleBlockSelectionIntoView()
    {
        if (_titleBlockGalleryScroll is null || _titleBlockGallery is null) return;
        var viewportWidth = Math.Max(1, _titleBlockGallery.Width - FoundryTheme.Space4 - 2);
        var maximum = Math.Max(0, _titleBlockPreviewTray.ContentWidth - viewportWidth);
        var target = Math.Clamp(_titleBlockPreviewTray.SelectedCenter - viewportWidth / 2, 0, maximum);
        _titleBlockGalleryScroll.ScrollPosition = new Point(target, 0);
    }

    private void ToggleNamedViewGallery(NamedViewSelectionDrawable selector, int detailIndex)
    {
        if (_namedViewGallery?.Visible == true && ReferenceEquals(_activeNamedViewSelector, selector))
        {
            HideNamedViewGallery();
            return;
        }

        _activeNamedViewSelector = selector;
        _activeDetailIndex = detailIndex;
        var targets = TargetDraftIndices();
        var values = targets.Select(index => _drafts[index].NamedViewsByDetail[detailIndex])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _updatingEditors = true;
        try
        {
            _namedViewPreviewTray.SelectedIndex = values.Length == 1 ? NamedViewIndex(values[0]) : 0;
        }
        finally
        {
            _updatingEditors = false;
        }
        _namedViewGalleryPrompt.Text = $"Choose a named view for {selector.DetailLabel}.";
        _displayModePicker.CloseResults();
        HideLayoutGallery();
        HideTitleBlockGallery();
        var gallery = EnsureNamedViewGallery();
        PositionNamedViewGallery();
        gallery.Show();
        gallery.BringToFront();
        RefreshNamedViewSelectorExpansion();
        _ = LoadNamedViewPreviewsAsync();
        Application.Instance.AsyncInvoke(() =>
        {
            ScrollNamedViewSelectionIntoView();
            _namedViewPreviewTray.Focus();
        });
    }

    private Form EnsureNamedViewGallery()
    {
        if (_namedViewGallery is not null) return _namedViewGallery;
        var scrollable = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = false,
            ExpandContentHeight = false,
            Content = _namedViewPreviewTray,
        };
        _namedViewGalleryScroll = scrollable;
        var gallery = new Form
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Resizable = false,
            Maximizable = false,
            Minimizable = false,
            Closeable = false,
            AutoSize = false,
            BackgroundColor = FoundryTheme.CanvasBorder,
            Padding = new Padding(1),
            Content = new Panel
            {
                BackgroundColor = FoundryTheme.CanvasSurface,
                Padding = new Padding(FoundryTheme.Space2),
                Content = new StackLayout
                {
                    Spacing = FoundryTheme.Space2,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        _namedViewGalleryPrompt,
                        new StackLayoutItem(scrollable, true),
                    },
                },
            },
        };
        gallery.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape) return;
            HideNamedViewGallery();
            eventArgs.Handled = true;
        };
        gallery.LostFocus += (_, _) => Application.Instance.AsyncInvoke(() =>
        {
            if (_namedViewGallery != gallery || !gallery.Visible) return;
            var mouse = Mouse.Position;
            var mousePoint = new Point((int)Math.Round(mouse.X), (int)Math.Round(mouse.Y));
            if (gallery.Bounds.Contains(mousePoint) || _namedViewPreviewTray.HasFocus) return;
            HideNamedViewGallery();
        });
        gallery.Closed += (_, _) =>
        {
            if (ReferenceEquals(_namedViewGallery, gallery))
            {
                _namedViewGallery = null;
                _namedViewGalleryScroll = null;
            }
            RefreshNamedViewSelectorExpansion();
        };
        _namedViewGallery = gallery;
        PositionNamedViewGallery();
        return gallery;
    }

    private void PositionNamedViewGallery()
    {
        if (_namedViewGallery is null) return;
        if (_activeNamedViewSelector is null) return;
        var anchor = _activeNamedViewSelector.PointToScreen(
            new PointF(0, _activeNamedViewSelector.Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(anchor)) ??
                     Screen.PrimaryScreen;
        var work = screen.WorkingArea;
        var left = (int)Math.Ceiling(work.Left);
        var top = (int)Math.Ceiling(work.Top);
        var right = (int)Math.Floor(work.Right);
        var bottom = (int)Math.Floor(work.Bottom);
        var desiredWidth = Math.Clamp(_namedViewPreviewTray.ContentWidth + FoundryTheme.Space4 + 2, 440, 780);
        var width = Math.Min(desiredWidth, Math.Max(320, right - left - FoundryTheme.Space4 * 2));
        var height = NamedViewPreviewTray.TrayHeight + 42 + FoundryTheme.Space4 + 2;
        var x = (int)Math.Round(anchor.X + _activeNamedViewSelector.Width - width);
        x = Math.Clamp(x, left + FoundryTheme.Space2, right - width - FoundryTheme.Space2);
        var y = (int)Math.Round(anchor.Y + FoundryTheme.Space1);
        if (y + height > bottom - FoundryTheme.Space2)
        {
            var selectorTop = _activeNamedViewSelector.PointToScreen(PointF.Empty).Y;
            y = (int)Math.Round(selectorTop - height - FoundryTheme.Space1);
        }
        y = Math.Clamp(y, top + FoundryTheme.Space2, bottom - height - FoundryTheme.Space2);
        _namedViewGallery.Size = new Size(width, height);
        _namedViewGallery.Location = new Point(x, y);
    }

    private void HideNamedViewGallery()
    {
        if (_namedViewGallery is not null) _namedViewGallery.Visible = false;
        RefreshNamedViewSelectorExpansion();
    }

    private void CloseNamedViewGallery()
    {
        if (_namedViewGallery is null) return;
        var gallery = _namedViewGallery;
        _namedViewGallery = null;
        _namedViewGalleryScroll = null;
        gallery.Close();
    }

    private void ScrollNamedViewSelectionIntoView()
    {
        if (_namedViewGalleryScroll is null || _namedViewGallery is null) return;
        var viewportWidth = Math.Max(1, _namedViewGallery.Width - FoundryTheme.Space4 - 2);
        var maximum = Math.Max(0, _namedViewPreviewTray.ContentWidth - viewportWidth);
        var target = Math.Clamp(_namedViewPreviewTray.SelectedCenter - viewportWidth / 2, 0, maximum);
        _namedViewGalleryScroll.ScrollPosition = new Point(target, 0);
    }

    private async Task LoadNamedViewPreviewsAsync()
    {
        if (_namedViewPreviewLoadStarted) return;
        _namedViewPreviewLoadStarted = true;
        foreach (var choice in _namedViewChoices.Where(choice => choice.Name is not null))
        {
            if (_namedViewPreviewCancellation.IsCancellationRequested) return;
            var key = new NamedViewThumbnailKey(
                _snapshot.DocumentRuntimeSerialNumber,
                choice.Name!,
                192,
                120,
                _snapshot.Revision);
            var result = await LayoutFoundryUiHost.CaptureNamedViewThumbnailAsync(
                new NamedViewThumbnailRequest(key),
                _namedViewPreviewCancellation.Token);
            if (!result.Succeeded || _namedViewPreviewCancellation.IsCancellationRequested) continue;
            if (_snapshot.DocumentRuntimeSerialNumber != result.Key.DocumentRuntimeSerialNumber ||
                !_snapshot.NamedViews.Contains(result.Key.NamedViewName))
                continue;
            _namedViewPreviewTray.SetPreview(result.Key.NamedViewName, new Bitmap(result.PngBytes!));
            foreach (var selector in _detailViewSelectors) selector.RefreshPreview();
        }
    }

    private void ApplyPaperToTargets()
    {
        if (_updatingEditors || _updatingPaper) return;
        var paper = CurrentPaper();
        SyncPaperSelectors(paper);
        _layoutPreviewTray.SetPaper(paper);
        _layoutSelectorPreview.SetPaper(paper);
        _titleBlockPreviewTray.SetPaper(paper);
        _titleBlockSelectorPreview.SetPaper(paper);
        ApplyToTargets(draft => draft with { Paper = paper });
    }

    private void ApplyDisplayModeToTargets()
    {
        if (_updatingEditors) return;
        var value = _displayModePicker.Text.Trim();
        Guid? displayModeId = null;
        if (!string.Equals(value, InheritDisplayMode, StringComparison.OrdinalIgnoreCase))
        {
            var match = _snapshot.DisplayModes.FirstOrDefault(pair => string.Equals(
                pair.Value, value, StringComparison.OrdinalIgnoreCase));
            if (match.Key == Guid.Empty)
            {
                RefreshPreview();
                return;
            }
            displayModeId = match.Key;
        }
        ApplyToTargets(draft => draft with { DisplayModeId = displayModeId });
    }

    private void ApplyDedicatedDetailLayerToTargets()
    {
        if (_updatingEditors) return;
        var useDedicatedLayer = _dedicatedDetailLayerCheck.Checked == true;
        ApplyToTargets(draft => draft with { UseDedicatedDetailLayer = useDedicatedLayer });
    }

    private void ApplyTitleBlockToTargets()
    {
        if (_updatingEditors) return;
        var titleBlock = _titleBlockChoices[Math.Max(0, _titleBlockPreviewTray.SelectedIndex)];
        ApplyToTargets(draft => draft with { TitleBlock = titleBlock });
    }

    private void OnTitleBlockSelectionChanged(object? sender, EventArgs eventArgs)
    {
        UpdateTitleBlockSelector();
        ApplyTitleBlockToTargets();
    }

    private void UpdateTitleBlockSelector()
    {
        _titleBlockSelectorPreview.SetSelection(
            Math.Max(0, _titleBlockPreviewTray.SelectedIndex),
            _titleBlockGallery?.Visible == true);
    }

    private void ApplyNamedViewToTargets()
    {
        if (_updatingEditors || _activeDetailIndex is not { } detailIndex) return;
        var namedView = _namedViewChoices[Math.Max(0, _namedViewPreviewTray.SelectedIndex)].Name;
        foreach (var index in TargetDraftIndices())
        {
            var values = _drafts[index].NamedViewsByDetail.ToArray();
            if (detailIndex >= values.Length) continue;
            values[detailIndex] = namedView;
            _drafts[index] = _drafts[index] with { NamedViewsByDetail = values };
        }
        _activeNamedViewSelector?.SetSelection(
            NamedViewIndex(namedView),
            _namedViewGallery?.Visible == true,
            mixed: false);
        RefreshPreview(refreshDetailAssignments: false);
    }

    private void OnNamedViewSelectionChanged(object? sender, EventArgs eventArgs)
    {
        ApplyNamedViewToTargets();
    }

    private void RefreshNamedViewSelectorExpansion()
    {
        foreach (var selector in _detailViewSelectors)
            selector.SetExpanded(_namedViewGallery?.Visible == true &&
                                 ReferenceEquals(selector, _activeNamedViewSelector));
    }

    private void ApplyToTargets(Func<CreationDraft, CreationDraft> update)
    {
        foreach (var index in TargetDraftIndices())
            _drafts[index] = update(_drafts[index]);
        RefreshPreview();
    }

    private Guid[] SelectedDraftIds() => _previewGrid.SelectedRows
        .Where(index => index >= 0 && index < _visiblePreviewRows.Count)
        .Select(index => _visiblePreviewRows[index].DraftId)
        .Distinct()
        .ToArray();

    private int[] SelectedDraftIndices()
    {
        var selected = SelectedDraftIds().ToHashSet();
        return _drafts.Select((draft, index) => (draft, index))
            .Where(item => selected.Contains(item.draft.DraftId))
            .Select(item => item.index)
            .Order()
            .ToArray();
    }

    private IReadOnlyList<int> TargetDraftIndices()
    {
        var selected = SelectedDraftIndices();
        if (selected.Length > 0) return selected;
        return _drafts.Select((draft, index) => (draft, index))
            .Where(item => _activeGroupFilter is null ||
                           LayoutGroupKey.For(item.draft.Layout) == _activeGroupFilter)
            .Select(item => item.index)
            .ToArray();
    }

    private void OnPreviewSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingPreviewSelection) return;
        var selected = SelectedDraftIndices();
        UpdateSelectionHint();
        if (selected.Length > 0) LoadEditors(_drafts[selected[0]]);
        RefreshDetailAssignments();
    }

    private void ClearPreviewSelection()
    {
        _previewGrid.SelectedRows = [];
        UpdateSelectionHint();
        RefreshDetailAssignments();
    }

    private void UpdateSelectionHint()
    {
        var selectedCount = SelectedDraftIndices().Length;
        _selectionHint.Text = selectedCount == 0
            ? _activeGroupFilter is null
                ? "No rows selected — property changes apply to all."
                : $"No rows selected — property changes apply to {ActiveGroupLabel()}."
            : $"{selectedCount} selected — property changes apply only to selected rows.";
        _clearSelectionButton.Enabled = selectedCount > 0;
    }

    private void EnsureActiveGroupExists()
    {
        if (_activeGroupFilter is null) return;
        if (_drafts.Any(draft => LayoutGroupKey.For(draft.Layout) == _activeGroupFilter)) return;
        _activeGroupFilter = null;
    }

    private void RefreshGroupChips()
    {
        _layoutGroupChips.Items.Clear();
        AddGroupChip(null, "All", _drafts.Count);
        foreach (var group in _drafts.GroupBy(draft => LayoutGroupKey.For(draft.Layout)))
            AddGroupChip(group.Key, group.First().Layout.Label, group.Count());
    }

    private void AddGroupChip(LayoutGroupKey? key, string label, int count)
    {
        var active = key == _activeGroupFilter;
        var text = $"{(active ? "✓ " : string.Empty)}{label}  ·  {count}";
        var button = new FoundryDialogButton(
            text,
            FoundryDialogButtonStyle.Secondary,
            Math.Max(68, (text.Length * 7) + 24))
        {
            ToolTip = key is null
                ? "Show the whole batch; with no selected rows, edits apply to all layouts."
                : $"Show only {label}; with no selected rows, edits apply to this group.",
        };
        button.Click += (_, _) => SwitchGroup(key);
        _layoutGroupChips.Items.Add(button);
    }

    private void SwitchGroup(LayoutGroupKey? key)
    {
        if (_activeGroupFilter == key) return;
        _activeGroupFilter = key;
        _previewGrid.SelectedRows = [];
        HideNamedViewGallery();
        RefreshPreview();
        var first = TargetDraftIndices().FirstOrDefault(-1);
        if (first >= 0) LoadEditors(_drafts[first]);
    }

    private string ActiveGroupLabel()
    {
        if (_activeGroupFilter is null) return "all layouts";
        return _drafts.FirstOrDefault(draft => LayoutGroupKey.For(draft.Layout) == _activeGroupFilter)
            ?.Layout.Label ?? "this layout group";
    }

    private void SelectDrafts(IReadOnlySet<Guid> draftIds)
    {
        _updatingPreviewSelection = true;
        try
        {
            _previewGrid.SelectedRows = _visiblePreviewRows
                .Select((row, index) => (row, index))
                .Where(item => draftIds.Contains(item.row.DraftId))
                .Select(item => item.index)
                .ToArray();
        }
        finally
        {
            _updatingPreviewSelection = false;
        }
        UpdateSelectionHint();
        RefreshDetailAssignments();
    }

    private void RefreshDetailAssignments()
    {
        if (_namedViewGallery?.Visible == true) _namedViewGallery.Visible = false;
        _activeNamedViewSelector = null;
        _activeDetailIndex = null;
        _detailViewSelectors.Clear();
        var targets = TargetDraftIndices();
        if (targets.Count == 0)
        {
            _detailViewAssignmentsHost.Content = FoundryTheme.MutedLabel("There are no layouts in this group.");
            return;
        }

        var groupKeys = targets.Select(index => LayoutGroupKey.For(_drafts[index].Layout))
            .Distinct()
            .Take(2)
            .ToArray();
        if (groupKeys.Length != 1)
        {
            _detailViewAssignmentsHost.Content = FoundryTheme.MutedLabel(
                "Choose a layout group to assign detail views.");
            return;
        }

        var layout = _drafts[targets[0]].Layout;
        var details = DetailLabels(layout);
        if (details.Count == 0)
        {
            _detailViewAssignmentsHost.Content = FoundryTheme.MutedLabel("This layout creates no details.");
            return;
        }

        for (var detailIndex = 0; detailIndex < details.Count; detailIndex++)
        {
            var values = targets.Select(index => _drafts[index].NamedViewsByDetail[detailIndex]).ToArray();
            var mixed = values.Skip(1).Any(value => !string.Equals(
                value, values[0], StringComparison.OrdinalIgnoreCase));
            var selector = new NamedViewSelectionDrawable(
                _namedViewChoices,
                _namedViewPreviewTray,
                details[detailIndex],
                NamedViewIndex(values[0]),
                mixed);
            var capturedIndex = detailIndex;
            selector.Activated += (_, _) => ToggleNamedViewGallery(selector, capturedIndex);
            _detailViewSelectors.Add(selector);
        }

        if (_detailViewSelectors.Count == 1)
        {
            _detailViewAssignmentsHost.Content = _detailViewSelectors[0];
            return;
        }

        var table = new TableLayout { Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2) };
        for (var index = 0; index < _detailViewSelectors.Count; index += 2)
        {
            table.Rows.Add(new TableRow(
                new TableCell(_detailViewSelectors[index], true),
                index + 1 < _detailViewSelectors.Count
                    ? new TableCell(_detailViewSelectors[index + 1], true)
                    : new TableCell(null, true)));
        }
        _detailViewAssignmentsHost.Content = table;
    }

    private static IReadOnlyList<string> DetailLabels(LayoutChoice layout)
    {
        if (layout.Template is { } template)
            return template.DetailSlots.Select((slot, index) =>
                string.IsNullOrWhiteSpace(slot.Name) ? $"Detail {index + 1}" : slot.Name).ToArray();
        var count = layout.BuiltInLayout switch
        {
            BuiltInLayoutKind.Blank => 0,
            BuiltInLayoutKind.SingleDetail => 1,
            BuiltInLayoutKind.TwoDetailsHorizontal or BuiltInLayoutKind.TwoDetailsVertical => 2,
            BuiltInLayoutKind.FourDetailsGrid => 4,
            _ => 0,
        };
        return Enumerable.Range(1, count).Select(index => $"Detail {index}").ToArray();
    }

    private static IReadOnlyList<string?> DefaultNamedViews(LayoutChoice layout) =>
        Enumerable.Repeat<string?>(null, DetailLabels(layout).Count).ToArray();

    private void LoadEditors(CreationDraft draft)
    {
        _updatingEditors = true;
        _updatingPaper = true;
        try
        {
            _layoutPreviewTray.SelectedIndex = Math.Max(0, Array.IndexOf(_layoutChoices, draft.Layout));
            _widthStepper.Value = draft.Paper.Width;
            _heightStepper.Value = draft.Paper.Height;
            _unitDropDown.SelectedIndex = UnitIndex(draft.Paper.UnitSystem);
            SyncPaperSelectors(draft.Paper);
            _layoutPreviewTray.SetPaper(draft.Paper);
            _layoutSelectorPreview.SetPaper(draft.Paper);
            _titleBlockPreviewTray.SetPaper(draft.Paper);
            _titleBlockSelectorPreview.SetPaper(draft.Paper);
            _displayModePicker.Text = draft.DisplayModeId is { } modeId
                ? _snapshot.DisplayModes.GetValueOrDefault(modeId) ?? InheritDisplayMode
                : InheritDisplayMode;
            _dedicatedDetailLayerCheck.Checked = draft.UseDedicatedDetailLayer;
            _titleBlockPreviewTray.SelectedIndex = Math.Max(0, Array.IndexOf(_titleBlockChoices, draft.TitleBlock));
            UpdateTitleBlockSelector();
        }
        finally
        {
            _updatingPaper = false;
            _updatingEditors = false;
        }
    }

    private void QueueApplyPaperPreset()
    {
        if (_updatingEditors) return;
        Application.Instance.AsyncInvoke(ApplyPaperPreset);
    }

    private void ApplyPaperPreset()
    {
        if (_updatingEditors || _updatingPaper || _paperPresetDropDown.SelectedIndex <= 0)
        {
            return;
        }
        _updatingPaper = true;
        var preset = PaperPresets[_paperPresetDropDown.SelectedIndex];
        var landscape = _orientationDropDown.SelectedIndex == 0;
        _widthStepper.Value = landscape ? Math.Max(preset.Width, preset.Height) : Math.Min(preset.Width, preset.Height);
        _heightStepper.Value = landscape ? Math.Min(preset.Width, preset.Height) : Math.Max(preset.Width, preset.Height);
        _unitDropDown.SelectedIndex = UnitIndex(preset.UnitSystem);
        _updatingPaper = false;
        ApplyPaperToTargets();
    }

    private PaperRecipe CurrentPaper() => new(
        _widthStepper.Value,
        _heightStepper.Value,
        Units[Math.Max(0, _unitDropDown.SelectedIndex)]);

    private void SyncPaperSelectors(PaperRecipe paper)
    {
        var wasUpdating = _updatingEditors;
        _updatingEditors = true;
        try
        {
            _paperPresetDropDown.SelectedIndex = PaperPresetIndex(paper);
            _orientationDropDown.SelectedIndex = paper.Width >= paper.Height ? 0 : 1;
        }
        finally
        {
            _updatingEditors = wasUpdating;
        }
    }

    private static int PaperPresetIndex(PaperRecipe paper)
    {
        for (var index = 1; index < PaperPresets.Length; index++)
        {
            var preset = PaperPresets[index];
            if (!string.Equals(preset.UnitSystem, paper.UnitSystem, StringComparison.OrdinalIgnoreCase)) continue;
            var sameOrientation = NearlyEqual(preset.Width, paper.Width) && NearlyEqual(preset.Height, paper.Height);
            var swappedOrientation = NearlyEqual(preset.Width, paper.Height) && NearlyEqual(preset.Height, paper.Width);
            if (sameOrientation || swappedOrientation) return index;
        }
        return 0;
    }

    private int NamedViewIndex(string? namedView)
    {
        if (string.IsNullOrWhiteSpace(namedView)) return 0;
        var index = Array.FindIndex(_namedViewChoices, choice => string.Equals(
            choice.Name,
            namedView,
            StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index;
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.0001;

    private string DisplayModeSummary(SheetTemplateRecipe template)
    {
        var names = template.DetailSlots
            .Select(slot => slot.DisplayModeId is { } id ? _snapshot.DisplayModes.GetValueOrDefault(id) : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return template.DetailSlots.Count == 0
            ? "—"
            : names.Length switch { 0 => "Rhino default", 1 => names[0]!, _ => "Mixed" };
    }

    private int PreferredFolderIndex(Guid? preferredFolderId)
    {
        var match = _folders.Select((folder, index) => (folder, index))
            .FirstOrDefault(item => item.folder.Id == preferredFolderId);
        return match.folder == default ? 0 : match.index;
    }

    private static Control Card(string title, Control content) => FoundryTheme.Surface(new StackLayout
    {
        Padding = new Padding(FoundryTheme.Space3),
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = title, Font = SystemFonts.Bold(13), TextColor = FoundryTheme.PrimaryText },
            content,
        },
    });

    private static GridColumn TextColumn(
        string header,
        Expression<Func<CreationPreviewRow, string>> property,
        int width,
        bool expand = false) => new()
    {
        HeaderText = header,
        Width = width,
        Expand = expand,
        DataCell = new TextBoxCell { Binding = Binding.Property(property) },
    };

    private static NumericStepper IntegerStepper(double value, double min, double max) => new()
    {
        Value = value, MinValue = min, MaxValue = max, DecimalPlaces = 0, Width = 76,
    };

    private static NumericStepper DimensionStepper(double value) => new()
    {
        Value = value, MinValue = 0.001, MaxValue = 1000000, DecimalPlaces = 3,
    };

    private static int UnitIndex(string? unit) => unit?.ToLowerInvariant() switch
    {
        "centimeters" => 1,
        "meters" => 2,
        "inches" => 3,
        "feet" => 4,
        _ => 0,
    };

    private static int FirstAvailablePageNumber(DocumentSnapshot snapshot)
    {
        var maximum = snapshot.Sheets.Values
            .Select(sheet => sheet.Name.StartsWith("Page ", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(sheet.Name[5..].Trim(), out var index)
                ? index
                : 0)
            .DefaultIfEmpty(0)
            .Max();
        return maximum + 1;
    }

    private static LayoutChoice[] LayoutChoices(DocumentSnapshot snapshot) =>
    [
        new LayoutChoice("Blank — no details", BuiltInLayoutKind.Blank, null, null),
        new LayoutChoice("1 Detail — Top", BuiltInLayoutKind.SingleDetail, null, null),
        new LayoutChoice("2 Details — Horizontal", BuiltInLayoutKind.TwoDetailsHorizontal, null, null),
        new LayoutChoice("2 Details — Vertical", BuiltInLayoutKind.TwoDetailsVertical, null, null),
        new LayoutChoice("4 Details — Grid", BuiltInLayoutKind.FourDetailsGrid, null, null),
        .. snapshot.Templates
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(template => new LayoutChoice(
                $"Template — {template.Name}", BuiltInLayoutKind.Blank, template.Id, template)),
    ];

    private static TitleBlockChoice[] TitleBlockChoices(DocumentSnapshot snapshot) =>
    [
        new TitleBlockChoice(true, null, null, "Use layout template", null),
        new TitleBlockChoice(false, null, null, "No title block", null),
        new TitleBlockChoice(false, null, BuiltInTitleBlockKind.CompactLowerRight,
            "Built-in — Compact lower-right", null),
        new TitleBlockChoice(false, null, BuiltInTitleBlockKind.FullWidthBottom,
            "Built-in — Full-width bottom band", null),
        new TitleBlockChoice(false, null, BuiltInTitleBlockKind.RightSidebar,
            "Built-in — Right-side vertical", null),
        new TitleBlockChoice(false, null, BuiltInTitleBlockKind.MinimalLowerRight,
            "Built-in — Minimal lower-right", null),
        .. snapshot.TitleBlockInstances.Values
            .Where(instance => instance.Transform is { Count: 16 })
            .OrderBy(instance => instance.InstanceDefinitionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.SourcePageName, StringComparer.OrdinalIgnoreCase)
            .Select(instance => new TitleBlockChoice(
                false,
                instance.InstanceObjectId,
                null,
                $"{instance.InstanceDefinitionName} — {instance.SourcePageName}",
                instance)),
    ];

    private static NamedViewChoice[] NamedViewChoices(DocumentSnapshot snapshot) =>
    [
        new NamedViewChoice(null, InheritNamedView),
        .. snapshot.NamedViews
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new NamedViewChoice(name, name)),
    ];

    private static IReadOnlyList<(Guid Id, string Label)> FolderChoices(DocumentSnapshot snapshot)
    {
        string Path(Guid id)
        {
            var parts = new List<string>();
            var seen = new HashSet<Guid>();
            while (snapshot.Folders.TryGetValue(id, out var folder) && seen.Add(id))
            {
                if (id != snapshot.RootFolderId) parts.Add(folder.Name);
                if (folder.ParentId is not { } parent) break;
                id = parent;
            }
            parts.Reverse();
            return parts.Count == 0 ? "Root" : string.Join(" / ", parts);
        }
        return snapshot.Folders.Values.OrderBy(folder => Path(folder.Id), StringComparer.OrdinalIgnoreCase)
            .Select(folder => (folder.Id, Path(folder.Id))).ToArray();
    }

    private static Control Header() => new StackLayout
    {
        Spacing = FoundryTheme.Space1,
        Items =
        {
            new Label { Text = "Create layouts", Font = SystemFonts.Bold(17), TextColor = FoundryTheme.PrimaryText },
            FoundryTheme.MutedLabel("Configure the batch once, review every resulting layout, then create it atomically."),
        },
    };

    private static readonly string[] Units = ["Millimeters", "Centimeters", "Meters", "Inches", "Feet"];

    private static readonly PaperPreset[] PaperPresets =
    [
        new("Custom", 0, 0, "Millimeters"),
        new("A0 — 841 × 1189 mm", 841, 1189, "Millimeters"),
        new("A1 — 594 × 841 mm", 594, 841, "Millimeters"),
        new("A2 — 420 × 594 mm", 420, 594, "Millimeters"),
        new("A3 — 297 × 420 mm", 297, 420, "Millimeters"),
        new("A4 — 210 × 297 mm", 210, 297, "Millimeters"),
        new("ANSI A — 8.5 × 11 in", 8.5, 11, "Inches"),
        new("ANSI B — 11 × 17 in", 11, 17, "Inches"),
        new("ANSI C — 17 × 22 in", 17, 22, "Inches"),
        new("ANSI D — 22 × 34 in", 22, 34, "Inches"),
    ];

    private sealed record LayoutChoice(
        string Label,
        BuiltInLayoutKind BuiltInLayout,
        Guid? TemplateId,
        SheetTemplateRecipe? Template);
    private sealed record TitleBlockChoice(
        bool UseTemplate,
        Guid? SourceInstanceObjectId,
        BuiltInTitleBlockKind? BuiltInKind,
        string Label,
        TitleBlockInstanceSnapshot? Instance);
    private sealed record NamedViewChoice(string? Name, string Label);
    private sealed record PaperPreset(string Label, double Width, double Height, string UnitSystem);
    private readonly record struct LayoutGroupKey(BuiltInLayoutKind? BuiltInLayout, Guid? TemplateId)
    {
        internal static LayoutGroupKey For(LayoutChoice layout) => layout.TemplateId is { } templateId
            ? new LayoutGroupKey(null, templateId)
            : new LayoutGroupKey(layout.BuiltInLayout, null);
    }
    private sealed record CreationDraft(
        Guid DraftId,
        LayoutChoice Layout,
        PaperRecipe Paper,
        Guid? DisplayModeId,
        bool UseDedicatedDetailLayer,
        TitleBlockChoice TitleBlock,
        IReadOnlyList<string?> NamedViewsByDetail)
    {
        internal LayoutCreationSpec ToSpec() => new(
            Quantity: 1,
            Paper: Paper,
            BuiltInLayout: Layout.BuiltInLayout,
            TemplateId: Layout.TemplateId,
            DetailDisplayModeId: DisplayModeId,
            UseTemplateTitleBlock: TitleBlock.UseTemplate,
            TitleBlockSourceInstanceObjectId: TitleBlock.SourceInstanceObjectId,
            BuiltInTitleBlock: TitleBlock.BuiltInKind,
            UseDedicatedDetailLayer: UseDedicatedDetailLayer,
            NamedViewsByDetail: NamedViewsByDetail);
    }

    private sealed record CreationPreviewRow(
        Guid DraftId,
        LayoutGroupKey GroupKey,
        string Index,
        string Name,
        string LayoutType,
        string Paper,
        string Details,
        string DetailLayer,
        string DisplayMode,
        string TitleBlock);

    private sealed class NamedViewSelectionDrawable : Drawable
    {
        private readonly NamedViewChoice[] _choices;
        private readonly NamedViewPreviewTray _previews;
        private readonly string _detailLabel;
        private readonly Font _titleFont = SystemFonts.Bold(9);
        private readonly Font _subtitleFont = SystemFonts.Default(8);
        private int _selectedIndex;
        private bool _mixed;
        private bool _expanded;

        internal NamedViewSelectionDrawable(
            NamedViewChoice[] choices,
            NamedViewPreviewTray previews,
            string detailLabel,
            int selectedIndex,
            bool mixed)
            : base(true)
        {
            _choices = choices;
            _previews = previews;
            _detailLabel = detailLabel;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            _mixed = mixed;
            CanFocus = true;
            Height = 78;
            MinimumSize = new Size(220, 78);
            BackgroundColor = FoundryTheme.CanvasSurface;
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? Activated;
        internal string DetailLabel => _detailLabel;

        internal void SetSelection(int selectedIndex, bool expanded, bool mixed)
        {
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
            _expanded = expanded;
            _mixed = mixed;
            Invalidate();
        }

        internal void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            Invalidate();
        }

        internal void RefreshPreview() => Invalidate();

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, HasFocus ? 2 : 1), bounds);

            var previewBounds = new RectangleF(12, 10, 92, 56);
            if (_mixed)
            {
                graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, previewBounds);
                graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), previewBounds);
                var mixedSize = graphics.MeasureString(_titleFont, "Mixed");
                graphics.DrawText(_titleFont, FoundryTheme.MutedText,
                    previewBounds.X + (previewBounds.Width - mixedSize.Width) / 2,
                    previewBounds.Y + (previewBounds.Height - mixedSize.Height) / 2,
                    "Mixed");
            }
            else
            {
                NamedViewPreviewTray.DrawPreview(
                    graphics,
                    _choices[_selectedIndex],
                    _previews.PreviewAt(_selectedIndex),
                    previewBounds);
            }
            var choice = _choices[_selectedIndex];
            var textWidth = Math.Max(20, Width - 142);
            graphics.DrawText(_titleFont, FoundryTheme.PrimaryText, 118, 23,
                LayoutPreviewTray.FitText(graphics, _titleFont, _detailLabel, textWidth));
            graphics.DrawText(_subtitleFont, FoundryTheme.MutedText, 118, 43,
                LayoutPreviewTray.FitText(graphics, _subtitleFont,
                    _mixed ? "Mixed" : choice.Label, textWidth));
            graphics.DrawText(SystemFonts.Default(10), FoundryTheme.MutedText,
                Math.Max(122, Width - 25), 30, _expanded ? "▴" : "▾");
        }

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
    }

    private sealed class NamedViewPreviewTray : Drawable
    {
        internal const int TrayHeight = 140;
        private const int TileWidth = 172;
        private const int TileHeight = 112;
        private const int ContentHeight = 120;
        private const int Gap = 8;
        private const int TrayPadding = 4;
        private readonly NamedViewChoice[] _choices;
        private readonly Font _titleFont = SystemFonts.Bold(8);
        private readonly Dictionary<string, Bitmap> _previews = new(StringComparer.OrdinalIgnoreCase);
        private int _selectedIndex;

        internal NamedViewPreviewTray(NamedViewChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            BackgroundColor = FoundryTheme.ContentBackground;
            Size = new Size(
                Math.Max(1, TrayPadding * 2 + choices.Length * TileWidth + Math.Max(0, choices.Length - 1) * Gap),
                ContentHeight);
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? SelectedIndexChanged;
        internal event EventHandler? SelectionCommitted;
        internal int ContentWidth => Size.Width;
        internal int SelectedCenter => TrayPadding + _selectedIndex * (TileWidth + Gap) + TileWidth / 2;

        internal int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var next = Math.Clamp(value, 0, Math.Max(0, _choices.Length - 1));
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal Bitmap? PreviewAt(int index)
        {
            if (index < 0 || index >= _choices.Length || _choices[index].Name is not { } name) return null;
            return _previews.GetValueOrDefault(name);
        }

        internal void SetPreview(string name, Bitmap bitmap)
        {
            if (_previews.Remove(name, out var previous)) previous.Dispose();
            _previews[name] = bitmap;
            Invalidate();
        }

        internal void DisposePreviews()
        {
            foreach (var bitmap in _previews.Values) bitmap.Dispose();
            _previews.Clear();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            graphics.FillRectangle(FoundryTheme.ContentBackground, eventArgs.ClipRectangle);
            for (var index = 0; index < _choices.Length; index++)
            {
                var tile = TileBounds(index);
                var selected = index == _selectedIndex;
                graphics.FillRectangle(
                    selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface,
                    tile);
                graphics.DrawRectangle(
                    new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                    tile);
                DrawPreview(
                    graphics,
                    _choices[index],
                    PreviewAt(index),
                    new RectangleF(tile.X + 8, tile.Y + 7, tile.Width - 16, 72));
                DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText,
                    _choices[index].Label, tile, tile.Bottom - 22);
            }
        }

        internal static void DrawPreview(
            Graphics graphics,
            NamedViewChoice choice,
            Bitmap? preview,
            RectangleF bounds)
        {
            graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
            if (preview is not null)
            {
                graphics.DrawImage(preview, bounds);
            }
            else if (choice.Name is null)
            {
                graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, bounds);
                var pen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 125), 1);
                var frame = new RectangleF(
                    bounds.X + bounds.Width * 0.2f,
                    bounds.Y + bounds.Height * 0.2f,
                    bounds.Width * 0.6f,
                    bounds.Height * 0.6f);
                graphics.DrawRectangle(pen, frame);
                graphics.DrawLine(pen, frame.Left, frame.Bottom,
                    frame.Left + frame.Width * 0.45f, frame.Top + frame.Height * 0.48f);
                graphics.DrawLine(pen, frame.Left + frame.Width * 0.45f,
                    frame.Top + frame.Height * 0.48f, frame.Right, frame.Bottom - frame.Height * 0.2f);
            }
            else
            {
                graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, bounds);
                var pen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 110), 1);
                graphics.DrawLine(pen, bounds.Left + 10, bounds.Bottom - 12,
                    bounds.Left + bounds.Width * 0.45f, bounds.Top + bounds.Height * 0.48f);
                graphics.DrawLine(pen, bounds.Left + bounds.Width * 0.45f,
                    bounds.Top + bounds.Height * 0.48f, bounds.Right - 10, bounds.Bottom - 18);
            }
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), bounds);
        }

        private static void DrawCentered(
            Graphics graphics,
            Font font,
            Color color,
            string text,
            RectangleF bounds,
            float y)
        {
            var fitted = LayoutPreviewTray.FitText(graphics, font, text, bounds.Width - 10);
            var size = graphics.MeasureString(font, fitted);
            graphics.DrawText(font, color, bounds.X + Math.Max(5, (bounds.Width - size.Width) / 2), y, fitted);
        }

        private RectangleF TileBounds(int index) => new(
            TrayPadding + index * (TileWidth + Gap),
            TrayPadding,
            TileWidth,
            TileHeight);

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var index = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
            if (index < 0 || index >= _choices.Length || !TileBounds(index).Contains(eventArgs.Location)) return;
            SelectedIndex = index;
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key is Keys.Enter or Keys.Space)
            {
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
                return;
            }
            var next = eventArgs.Key switch
            {
                Keys.Left => _selectedIndex - 1,
                Keys.Right => _selectedIndex + 1,
                Keys.Home => 0,
                Keys.End => _choices.Length - 1,
                _ => _selectedIndex,
            };
            next = Math.Clamp(next, 0, Math.Max(0, _choices.Length - 1));
            if (next == _selectedIndex) return;
            SelectedIndex = next;
            eventArgs.Handled = true;
        }
    }

    private sealed class TitleBlockSelectionDrawable : Drawable
    {
        private readonly TitleBlockChoice[] _choices;
        private readonly Font _titleFont = SystemFonts.Bold(9);
        private readonly Font _subtitleFont = SystemFonts.Default(8);
        private int _selectedIndex;
        private bool _expanded;
        private PaperRecipe _paper = new(594, 420, "Millimeters");

        internal TitleBlockSelectionDrawable(TitleBlockChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            Height = 78;
            Size = new Size(220, 78);
            BackgroundColor = FoundryTheme.CanvasSurface;
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? Activated;

        internal void SetSelection(int selectedIndex, bool expanded)
        {
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
            _expanded = expanded;
            Invalidate();
        }

        internal void SetPaper(PaperRecipe paper)
        {
            _paper = paper;
            Invalidate();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, HasFocus ? 2 : 1), bounds);
            var page = TitleBlockPreviewTray.PageBounds(_paper, new RectangleF(12, 10, 78, 56));
            TitleBlockPreviewTray.DrawTitleBlock(graphics, _choices[_selectedIndex], _paper, page);
            var parts = _choices[_selectedIndex].Label.Split([" — "], 2, StringSplitOptions.None);
            var textWidth = Math.Max(20, Width - 132);
            graphics.DrawText(_titleFont, FoundryTheme.PrimaryText, 106, 23,
                LayoutPreviewTray.FitText(graphics, _titleFont, parts[0], textWidth));
            if (parts.Length > 1)
                graphics.DrawText(_subtitleFont, FoundryTheme.MutedText, 106, 43,
                    LayoutPreviewTray.FitText(graphics, _subtitleFont, parts[1], textWidth));
            graphics.DrawText(SystemFonts.Default(10), FoundryTheme.MutedText,
                Math.Max(110, Width - 25), 30, _expanded ? "▴" : "▾");
        }

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
    }

    private sealed class TitleBlockPreviewTray : Drawable
    {
        internal const int TrayHeight = 140;
        private const int TileWidth = 156;
        private const int TileHeight = 112;
        private const int Gap = 8;
        private const int TrayPadding = 4;
        private readonly TitleBlockChoice[] _choices;
        private readonly Font _titleFont = SystemFonts.Bold(8);
        private readonly Font _subtitleFont = SystemFonts.Default(8);
        private int _selectedIndex;
        private PaperRecipe _paper = new(594, 420, "Millimeters");

        internal TitleBlockPreviewTray(TitleBlockChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            BackgroundColor = FoundryTheme.ContentBackground;
            Size = new Size(
                Math.Max(1, TrayPadding * 2 + choices.Length * TileWidth + Math.Max(0, choices.Length - 1) * Gap),
                120);
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? SelectedIndexChanged;
        internal event EventHandler? SelectionCommitted;
        internal int ContentWidth => Size.Width;
        internal int SelectedCenter => TrayPadding + _selectedIndex * (TileWidth + Gap) + TileWidth / 2;

        internal int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var next = Math.Clamp(value, 0, Math.Max(0, _choices.Length - 1));
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal void SetPaper(PaperRecipe paper)
        {
            _paper = paper;
            Invalidate();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            graphics.FillRectangle(FoundryTheme.ContentBackground, eventArgs.ClipRectangle);
            for (var index = 0; index < _choices.Length; index++)
            {
                var tile = TileBounds(index);
                var selected = index == _selectedIndex;
                graphics.FillRectangle(selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface, tile);
                graphics.DrawRectangle(
                    new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                    tile);
                var page = PageBounds(_paper, new RectangleF(tile.X + 25, tile.Y + 8, tile.Width - 50, 56));
                DrawTitleBlock(graphics, _choices[index], _paper, page);
                var parts = _choices[index].Label.Split([" — "], 2, StringSplitOptions.None);
                DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText, parts[0], tile, tile.Bottom - 31);
                if (parts.Length > 1)
                    DrawCentered(graphics, _subtitleFont, FoundryTheme.MutedText, parts[1], tile, tile.Bottom - 17);
            }
        }

        internal static RectangleF PageBounds(PaperRecipe paper, RectangleF available)
        {
            var paperWidth = Math.Max(0.001, paper.Width);
            var paperHeight = Math.Max(0.001, paper.Height);
            var scale = Math.Min(available.Width / paperWidth, available.Height / paperHeight);
            var width = (float)(paperWidth * scale);
            var height = (float)(paperHeight * scale);
            return new RectangleF(
                available.X + (available.Width - width) / 2,
                available.Y + (available.Height - height) / 2,
                width,
                height);
        }

        internal static void DrawTitleBlock(
            Graphics graphics,
            TitleBlockChoice choice,
            PaperRecipe paper,
            RectangleF page)
        {
            graphics.FillRectangle(Color.FromArgb(50, 0, 0, 0), page.X + 2, page.Y + 3, page.Width, page.Height);
            graphics.FillRectangle(Colors.White, page);
            graphics.DrawRectangle(new Pen(Color.FromArgb(160, 90, 90, 90), 1), page);
            if (!choice.UseTemplate && choice.SourceInstanceObjectId is null && choice.BuiltInKind is null)
            {
                graphics.DrawLine(new Pen(Color.FromArgb(180, 145, 145, 145), 2),
                    page.X + page.Width * 0.28f, page.Y + page.Height * 0.72f,
                    page.X + page.Width * 0.72f, page.Y + page.Height * 0.28f);
                return;
            }

            if (choice.BuiltInKind is { } kind)
            {
                try
                {
                    var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, paper);
                    float X(double value) => page.X + (float)(value / paper.Width * page.Width);
                    float Y(double value) => page.Bottom - (float)(value / paper.Height * page.Height);
                    float W(double value) => (float)(value / paper.Width * page.Width);
                    float H(double value) => (float)(value / paper.Height * page.Height);
                    var managedBlock = new RectangleF(
                        X(layout.Block.Left),
                        Y(layout.Block.Top),
                        W(layout.Block.Width),
                        H(layout.Block.Height));
                    var pen = new Pen(Color.FromArgb(190, 95, 98, 102), 1);
                    graphics.DrawRectangle(pen, managedBlock);
                    graphics.DrawLine(pen, managedBlock.X, managedBlock.Y + managedBlock.Height * 0.32f,
                        managedBlock.Right, managedBlock.Y + managedBlock.Height * 0.32f);
                    graphics.DrawLine(pen, managedBlock.X + managedBlock.Width * 0.62f, managedBlock.Y,
                        managedBlock.X + managedBlock.Width * 0.62f, managedBlock.Bottom);
                    graphics.DrawLine(pen, managedBlock.X, managedBlock.Y + managedBlock.Height * 0.68f,
                        managedBlock.Right, managedBlock.Y + managedBlock.Height * 0.68f);
                    return;
                }
                catch (Exception)
                {
                    return;
                }
            }

            var margin = Math.Max(2, page.Width * 0.035f);
            graphics.DrawRectangle(new Pen(Color.FromArgb(180, 115, 115, 115), 1),
                page.X + margin, page.Y + margin, page.Width - margin * 2, page.Height - margin * 2);
            var blockWidth = page.Width * (choice.UseTemplate ? 0.42f : 0.48f);
            var blockHeight = page.Height * 0.24f;
            var block = new RectangleF(
                page.Right - margin - blockWidth,
                page.Bottom - margin - blockHeight,
                blockWidth,
                blockHeight);
            graphics.FillRectangle(Color.FromArgb(255, 226, 228, 231), block);
            graphics.DrawRectangle(new Pen(Color.FromArgb(190, 95, 98, 102), 1), block);
            graphics.DrawLine(new Pen(Color.FromArgb(150, 95, 98, 102), 1),
                block.X + block.Width * 0.62f, block.Y, block.X + block.Width * 0.62f, block.Bottom);
            graphics.DrawLine(new Pen(Color.FromArgb(150, 95, 98, 102), 1),
                block.X, block.Y + block.Height * 0.5f, block.Right, block.Y + block.Height * 0.5f);
        }

        private static void DrawCentered(
            Graphics graphics,
            Font font,
            Color color,
            string text,
            RectangleF bounds,
            float y)
        {
            var fitted = LayoutPreviewTray.FitText(graphics, font, text, bounds.Width - 8);
            var size = graphics.MeasureString(font, fitted);
            graphics.DrawText(font, color, bounds.X + Math.Max(4, (bounds.Width - size.Width) / 2), y, fitted);
        }

        private RectangleF TileBounds(int index) => new(
            TrayPadding + index * (TileWidth + Gap),
            TrayPadding,
            TileWidth,
            TileHeight);

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var index = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
            if (index < 0 || index >= _choices.Length || !TileBounds(index).Contains(eventArgs.Location)) return;
            SelectedIndex = index;
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key is Keys.Enter or Keys.Space)
            {
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
                return;
            }
            var next = eventArgs.Key switch
            {
                Keys.Left => _selectedIndex - 1,
                Keys.Right => _selectedIndex + 1,
                Keys.Home => 0,
                Keys.End => _choices.Length - 1,
                _ => _selectedIndex,
            };
            next = Math.Clamp(next, 0, Math.Max(0, _choices.Length - 1));
            if (next == _selectedIndex) return;
            SelectedIndex = next;
            eventArgs.Handled = true;
        }
    }

    private sealed class LayoutSelectionDrawable : Drawable
    {
        private readonly LayoutChoice[] _choices;
        private readonly Font _titleFont = SystemFonts.Bold(9);
        private readonly Font _subtitleFont = SystemFonts.Default(8);
        private int _selectedIndex;
        private bool _expanded;
        private PaperRecipe _paper = new(594, 420, "Millimeters");

        internal LayoutSelectionDrawable(LayoutChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            Height = 78;
            MinimumSize = new Size(220, 78);
            BackgroundColor = FoundryTheme.CanvasSurface;
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? Activated;

        internal void SetSelection(int selectedIndex, bool expanded)
        {
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
            _expanded = expanded;
            Invalidate();
        }

        internal void SetPaper(PaperRecipe paper)
        {
            _paper = paper;
            Invalidate();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            graphics.FillRectangle(FoundryTheme.CanvasSurface, bounds);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, HasFocus ? 2 : 1), bounds);

            var paperWidth = Math.Max(0.001, _paper.Width);
            var paperHeight = Math.Max(0.001, _paper.Height);
            const float availableWidth = 78;
            const float availableHeight = 52;
            var scale = Math.Min(availableWidth / paperWidth, availableHeight / paperHeight);
            var page = new RectangleF(
                12 + (availableWidth - (float)(paperWidth * scale)) / 2,
                13 + (availableHeight - (float)(paperHeight * scale)) / 2,
                (float)(paperWidth * scale),
                (float)(paperHeight * scale));
            graphics.FillRectangle(Color.FromArgb(50, 0, 0, 0), page.X + 2, page.Y + 3, page.Width, page.Height);
            graphics.FillRectangle(Colors.White, page);
            graphics.DrawRectangle(new Pen(Color.FromArgb(145, 95, 95, 95), 1), page);
            foreach (var detail in LayoutPreviewTray.DetailBounds(_choices[_selectedIndex], page))
            {
                graphics.FillRectangle(Color.FromArgb(255, 220, 223, 226), detail);
                graphics.DrawRectangle(new Pen(Color.FromArgb(180, 95, 98, 102), 1), detail);
            }

            var parts = _choices[_selectedIndex].Label.Split([" — "], 2, StringSplitOptions.None);
            var textWidth = Math.Max(20, Width - 132);
            var title = LayoutPreviewTray.FitText(graphics, _titleFont, parts[0], textWidth);
            graphics.DrawText(_titleFont, FoundryTheme.PrimaryText, 106, 23, title);
            if (parts.Length > 1)
            {
                var subtitle = LayoutPreviewTray.FitText(graphics, _subtitleFont, parts[1], textWidth);
                graphics.DrawText(_subtitleFont, FoundryTheme.MutedText, 106, 43, subtitle);
            }
            graphics.DrawText(SystemFonts.Default(10), FoundryTheme.MutedText,
                Math.Max(110, Width - 25), 30, _expanded ? "▴" : "▾");
        }

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
            Activated?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
    }

    private sealed class LayoutPreviewTray : Drawable
    {
        internal const int TrayHeight = 140;
        private const int TileWidth = 136;
        private const int TileHeight = 112;
        private const int ContentHeight = 120;
        private const int Gap = 8;
        private const int TrayPadding = 4;
        private readonly LayoutChoice[] _choices;
        private readonly Font _titleFont = SystemFonts.Bold(8);
        private readonly Font _subtitleFont = SystemFonts.Default(8);
        private int _selectedIndex;
        private PaperRecipe _paper = new(594, 420, "Millimeters");

        internal LayoutPreviewTray(LayoutChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            BackgroundColor = FoundryTheme.ContentBackground;
            Size = new Size(
                Math.Max(1, TrayPadding * 2 + choices.Length * TileWidth + Math.Max(0, choices.Length - 1) * Gap),
                ContentHeight);
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? SelectedIndexChanged;
        internal event EventHandler? SelectionCommitted;

        internal int ContentWidth => Size.Width;
        internal int SelectedCenter => TrayPadding + _selectedIndex * (TileWidth + Gap) + TileWidth / 2;

        internal int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                var next = Math.Clamp(value, 0, Math.Max(0, _choices.Length - 1));
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal void SetPaper(PaperRecipe paper)
        {
            _paper = paper;
            Invalidate();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            graphics.FillRectangle(FoundryTheme.ContentBackground, eventArgs.ClipRectangle);
            for (var index = 0; index < _choices.Length; index++) DrawTile(graphics, index);
        }

        private void DrawTile(Graphics graphics, int index)
        {
            var tile = TileBounds(index);
            var selected = index == _selectedIndex;
            graphics.FillRectangle(
                selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface,
                tile);
            graphics.DrawRectangle(
                new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                tile);

            var page = PageBounds(tile);
            graphics.FillRectangle(Color.FromArgb(55, 0, 0, 0), page.X + 2, page.Y + 3, page.Width, page.Height);
            graphics.FillRectangle(Colors.White, page);
            graphics.DrawRectangle(new Pen(Color.FromArgb(145, 95, 95, 95), 1), page);
            foreach (var detail in DetailBounds(_choices[index], page))
            {
                graphics.FillRectangle(Color.FromArgb(255, 220, 223, 226), detail);
                graphics.DrawRectangle(new Pen(Color.FromArgb(180, 95, 98, 102), 1), detail);
            }

            var parts = _choices[index].Label.Split([" — "], 2, StringSplitOptions.None);
            DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText, parts[0], tile, tile.Bottom - 31);
            if (parts.Length > 1)
                DrawCentered(graphics, _subtitleFont, FoundryTheme.MutedText, parts[1], tile, tile.Bottom - 17);
        }

        private RectangleF PageBounds(RectangleF tile)
        {
            const float availableWidth = 94;
            const float availableHeight = 54;
            var paperWidth = Math.Max(0.001, _paper.Width);
            var paperHeight = Math.Max(0.001, _paper.Height);
            var scale = Math.Min(availableWidth / paperWidth, availableHeight / paperHeight);
            var width = (float)Math.Max(12, paperWidth * scale);
            var height = (float)Math.Max(12, paperHeight * scale);
            return new RectangleF(
                tile.X + (tile.Width - width) / 2,
                tile.Y + 9 + (availableHeight - height) / 2,
                width,
                height);
        }

        internal static IReadOnlyList<RectangleF> DetailBounds(LayoutChoice choice, RectangleF page)
        {
            if (choice.Template is { } template)
            {
                var width = Math.Max(0.001, template.Paper.Width);
                var height = Math.Max(0.001, template.Paper.Height);
                return template.DetailSlots.Select(slot => FromNormalized(
                    page,
                    slot.Left / width,
                    slot.Bottom / height,
                    slot.Right / width,
                    slot.Top / height)).ToArray();
            }

            const double margin = 0.05;
            const double gap = 0.02;
            var halfGap = gap / 2;
            return choice.BuiltInLayout switch
            {
                BuiltInLayoutKind.Blank => [],
                BuiltInLayoutKind.SingleDetail => [FromNormalized(page, margin, margin, 1 - margin, 1 - margin)],
                BuiltInLayoutKind.TwoDetailsHorizontal =>
                [
                    FromNormalized(page, margin, 0.5 + halfGap, 1 - margin, 1 - margin),
                    FromNormalized(page, margin, margin, 1 - margin, 0.5 - halfGap),
                ],
                BuiltInLayoutKind.TwoDetailsVertical =>
                [
                    FromNormalized(page, margin, margin, 0.5 - halfGap, 1 - margin),
                    FromNormalized(page, 0.5 + halfGap, margin, 1 - margin, 1 - margin),
                ],
                BuiltInLayoutKind.FourDetailsGrid =>
                [
                    FromNormalized(page, margin, 0.5 + halfGap, 0.5 - halfGap, 1 - margin),
                    FromNormalized(page, 0.5 + halfGap, 0.5 + halfGap, 1 - margin, 1 - margin),
                    FromNormalized(page, margin, margin, 0.5 - halfGap, 0.5 - halfGap),
                    FromNormalized(page, 0.5 + halfGap, margin, 1 - margin, 0.5 - halfGap),
                ],
                _ => [],
            };
        }

        private static RectangleF FromNormalized(
            RectangleF page,
            double left,
            double bottom,
            double right,
            double top) => new(
                page.X + (float)(left * page.Width),
                page.Bottom - (float)(top * page.Height),
                (float)((right - left) * page.Width),
                (float)((top - bottom) * page.Height));

        private static void DrawCentered(
            Graphics graphics,
            Font font,
            Color color,
            string text,
            RectangleF bounds,
            float y)
        {
            var fitted = FitText(graphics, font, text, bounds.Width - 8);
            var size = graphics.MeasureString(font, fitted);
            graphics.DrawText(font, color, bounds.X + Math.Max(4, (bounds.Width - size.Width) / 2), y, fitted);
        }

        internal static string FitText(Graphics graphics, Font font, string text, float maximumWidth)
        {
            if (graphics.MeasureString(font, text).Width <= maximumWidth) return text;
            const string ellipsis = "…";
            for (var length = text.Length - 1; length > 0; length--)
            {
                var candidate = text[..length].TrimEnd() + ellipsis;
                if (graphics.MeasureString(font, candidate).Width <= maximumWidth) return candidate;
            }
            return ellipsis;
        }

        private RectangleF TileBounds(int index) => new(
            TrayPadding + index * (TileWidth + Gap),
            TrayPadding,
            TileWidth,
            TileHeight);

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var stride = TileWidth + Gap;
            var index = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / stride);
            if (index < 0 || index >= _choices.Length || !TileBounds(index).Contains(eventArgs.Location)) return;
            SelectedIndex = index;
            SelectionCommitted?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            var next = eventArgs.Key switch
            {
                Keys.Left => _selectedIndex - 1,
                Keys.Right => _selectedIndex + 1,
                Keys.Home => 0,
                Keys.End => _choices.Length - 1,
                _ => _selectedIndex,
            };
            if (eventArgs.Key is Keys.Enter or Keys.Space)
            {
                SelectionCommitted?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
                return;
            }
            next = Math.Clamp(next, 0, Math.Max(0, _choices.Length - 1));
            if (next == _selectedIndex) return;
            SelectedIndex = next;
            eventArgs.Handled = true;
        }
    }
}
