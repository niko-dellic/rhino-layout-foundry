using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;

namespace RhinoLayoutFoundry.UI;

internal sealed class BatchCreateLayoutsDialog : Dialog
{
    private const string InheritDisplayMode = "Use layout/template setting";
    private const string InheritNamedView = "Use layout/template camera";
    private readonly DocumentSnapshot _snapshot;
    private readonly IReadOnlyList<(Guid Id, string Label)> _folders;
    private readonly LayoutChoice[] _layoutChoices;
    private readonly TitleBlockChoice[] _titleBlockChoices;
    private readonly DropDown _destinationDropDown;
    private readonly NumericStepper _quantityStepper;
    private readonly TextBox _patternBox;
    private readonly NumericStepper _startStepper;
    private readonly NumericStepper _stepStepper;
    private readonly LayoutPreviewTray _layoutPreviewTray;
    private readonly Button _layoutSelectorButton;
    private readonly DropDown _paperPresetDropDown;
    private readonly DropDown _orientationDropDown;
    private readonly NumericStepper _widthStepper;
    private readonly NumericStepper _heightStepper;
    private readonly DropDown _unitDropDown;
    private readonly FilteredPicker _displayModePicker;
    private readonly FilteredPicker _titleBlockPicker;
    private readonly DropDown _namedViewDropDown;
    private readonly GridView _previewGrid;
    private readonly Label _countLabel;
    private readonly Label _selectionHint;
    private readonly Button _editAllButton;
    private readonly Label _status;
    private readonly Button _createButton;
    private readonly List<CreationDraft> _drafts = [];
    private Form? _layoutGallery;
    private Scrollable? _layoutGalleryScroll;
    private bool _updatingPaper;
    private bool _updatingEditors;
    private bool _updatingPreviewSelection;

    internal BatchCreateLayoutsDialog(DocumentSnapshot snapshot, Guid? preferredFolderId)
    {
        _snapshot = snapshot;
        _folders = FolderChoices(snapshot);
        _layoutChoices = LayoutChoices(snapshot);
        _titleBlockChoices = TitleBlockChoices(snapshot);
        Title = "Create layouts";
        MinimumSize = new Size(1080, 760);
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
        _layoutSelectorButton = FoundryTheme.ConfigureButton(new Button(), 210);
        _layoutSelectorButton.Height = 36;
        _layoutSelectorButton.ToolTip = "Open the layout gallery";
        UpdateLayoutSelector();
        _displayModePicker = new FilteredPicker(
            new[] { InheritDisplayMode }.Concat(snapshot.DisplayModes.Values),
            "Search display modes");
        _displayModePicker.Text = InheritDisplayMode;
        _titleBlockPicker = new FilteredPicker(
            _titleBlockChoices.Select(choice => choice.Label),
            "Search title blocks");
        _titleBlockPicker.Text = _titleBlockChoices[0].Label;
        _namedViewDropDown = new DropDown
        {
            DataStore = new[] { InheritNamedView }
                .Concat(snapshot.NamedViews.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                .ToArray(),
            SelectedIndex = 0,
        };
        _previewGrid = CreatePreviewGrid();
        _countLabel = new Label { Font = SystemFonts.Bold(13), TextColor = FoundryTheme.PrimaryText };
        _selectionHint = FoundryTheme.MutedLabel();
        _editAllButton = FoundryTheme.ConfigureButton(new Button { Text = "Edit all" }, 72);
        _editAllButton.Visible = false;
        _editAllButton.Click += (_, _) => ClearPreviewSelection();
        _status = FoundryTheme.MutedLabel();
        _status.Wrap = WrapMode.Word;
        _createButton = FoundryTheme.ConfigureButton(new Button { Text = "Create layouts" }, 118);
        var cancel = FoundryTheme.ConfigureButton(new Button { Text = "Cancel" });
        cancel.Click += (_, _) => Close();
        _createButton.Click += async (_, _) => await CreateAsync();
        AbortButton = cancel;

        _drafts.Add(DraftFromEditors());

        _destinationDropDown.SelectedIndexChanged += (_, _) => QueueRefreshPreview();
        _quantityStepper.ValueChanged += (_, _) => ResizeDrafts();
        _startStepper.ValueChanged += (_, _) => RefreshPreview();
        _stepStepper.ValueChanged += (_, _) => RefreshPreview();
        _widthStepper.ValueChanged += (_, _) => ApplyPaperToTargets();
        _heightStepper.ValueChanged += (_, _) => ApplyPaperToTargets();
        _unitDropDown.SelectedIndexChanged += (_, _) => ApplyPaperToTargets();
        _namedViewDropDown.SelectedIndexChanged += (_, _) => ApplyNamedViewToTargets();
        _patternBox.TextChanged += (_, _) => RefreshPreview();
        _paperPresetDropDown.SelectedIndexChanged += (_, _) => QueueApplyPaperPreset();
        _orientationDropDown.SelectedIndexChanged += (_, _) => QueueApplyPaperPreset();
        _layoutSelectorButton.Click += (_, _) => ToggleLayoutGallery();
        _layoutPreviewTray.SelectedIndexChanged += OnLayoutSelectionChanged;
        _layoutPreviewTray.SelectionCommitted += (_, _) => HideLayoutGallery();
        _displayModePicker.ValueChanged += (_, _) => ApplyDisplayModeToTargets();
        _titleBlockPicker.ValueChanged += (_, _) => ApplyTitleBlockToTargets();
        _previewGrid.SelectedRowsChanged += OnPreviewSelectionChanged;
        _displayModePicker.Opened += (_, _) => _titleBlockPicker.CloseResults();
        _titleBlockPicker.Opened += (_, _) => _displayModePicker.CloseResults();
        Closed += (_, _) => CloseLayoutGallery();
        LocationChanged += (_, _) => PositionLayoutGallery();

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                Header(),
                new TableLayout
                {
                    Spacing = new Size(FoundryTheme.Space3, FoundryTheme.Space3),
                    Rows =
                    {
                        new TableRow(
                            new TableCell(Card("Batch", CreateBatchEditor()), true),
                            new TableCell(Card("Page size", CreatePaperEditor()), true),
                            new TableCell(Card("Layout", CreateLayoutEditor()), true)),
                        new TableRow(
                            new TableCell(Card("Details", CreateDetailEditor()), true),
                            new TableCell(Card("Title block", CreateTitleBlockEditor()), true),
                            new TableCell(Card("View", CreateNamedViewEditor()), true)),
                    },
                },
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
                        _editAllButton,
                    },
                },
                new StackLayoutItem(_previewGrid, true),
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

    private Control CreateBatchEditor() => new TableLayout
    {
        Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2),
        Rows =
        {
            new TableRow(new Label { Text = "Destination" }, new TableCell(_destinationDropDown, true)),
            new TableRow(new Label { Text = "Quantity" }, _quantityStepper),
            new TableRow(new Label { Text = "Name / pattern" }, new TableCell(_patternBox, true)),
            new TableRow(new Label { Text = "Start / step" }, new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space1,
                Items = { _startStepper, _stepStepper },
            }),
        },
    };

    private Control CreateLayoutEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Choose a built-in arrangement or a captured layout template." },
            _layoutSelectorButton,
        },
    };

    private Control CreatePaperEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            _paperPresetDropDown,
            _orientationDropDown,
            new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
                Rows =
                {
                    new TableRow(new Label { Text = "Width" }, _widthStepper),
                    new TableRow(new Label { Text = "Height" }, _heightStepper),
                    new TableRow(new Label { Text = "Units" }, _unitDropDown),
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
        },
    };

    private Control CreateTitleBlockEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Use the template, no block, or copy a page-space block instance." },
            _titleBlockPicker,
        },
    };

    private Control CreateNamedViewEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Items =
        {
            new Label { Text = "Optionally apply one named view to every created detail." },
            _namedViewDropDown,
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
        grid.Columns.Add(TextColumn("Display mode", row => row.DisplayMode, 150, true));
        grid.Columns.Add(TextColumn("Title block", row => row.TitleBlock, 160, true));
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
            CreationSpecs: _drafts.Select(draft => draft.ToSpec()).ToArray());
    }

    private void RefreshPreview()
    {
        if (_updatingPaper || _folders.Count == 0) return;
        var selectedRows = SelectedDraftIndices();
        var plan = new BatchCreateSheetsPlanner().Plan(Request(), _snapshot);
        var changes = plan.Changes.OfType<CreateSheetFromTemplateChange>().ToArray();
        var rows = changes.Select((change, index) => new CreationPreviewRow(
            (index + 1).ToString(),
            change.Name,
            change.Template.Name,
            $"{change.Template.Paper.Width:0.###} × {change.Template.Paper.Height:0.###} {change.Template.Paper.UnitSystem}",
            change.Template.DetailSlots.Count.ToString(),
            DisplayModeSummary(change.Template),
            change.Template.TitleBlock?.InstanceDefinitionName ?? "None")).ToArray();
        _updatingPreviewSelection = true;
        try
        {
            _previewGrid.DataStore = rows;
            _previewGrid.SelectedRows = selectedRows.Where(index => index < rows.Length).ToArray();
        }
        finally
        {
            _updatingPreviewSelection = false;
        }
        CreatedCount = changes.Length;
        _countLabel.Text = $"Layouts to create  ·  {CreatedCount}";
        UpdateSelectionHint();
        var pickerError = PickerError();
        var diagnostics = string.Join(" ", plan.Diagnostics
            .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(item => item.Message));
        _status.Text = pickerError ?? diagnostics;
        _createButton.Text = CreatedCount == 1 ? "Create layout" : $"Create {CreatedCount} layouts";
        _createButton.Enabled = plan.CanApply && pickerError is null;
    }

    private void QueueRefreshPreview()
    {
        Application.Instance.AsyncInvoke(RefreshPreview);
    }

    private string? PickerError()
    {
        if (!string.Equals(_displayModePicker.Text.Trim(), InheritDisplayMode, StringComparison.OrdinalIgnoreCase) &&
            !_snapshot.DisplayModes.Values.Any(name => string.Equals(
                name, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Choose an available Rhino display mode or use the layout/template setting.";
        if (!_titleBlockChoices.Any(choice => string.Equals(
                choice.Label, _titleBlockPicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Choose Use layout template, No title block, or an available title-block instance.";
        return null;
    }

    private async Task CreateAsync()
    {
        _createButton.Enabled = false;
        _status.Text = $"Creating {CreatedCount} layout{(CreatedCount == 1 ? string.Empty : "s")}…";
        var result = await LayoutFoundryUiHost.BatchCreateSheetsAsync(Request());
        if (!result.Succeeded)
        {
            _status.Text = string.Join(" ", result.Diagnostics.Select(item => item.Message));
            RefreshPreview();
            return;
        }
        Succeeded = true;
        Close();
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
        var titleBlock = _titleBlockChoices.FirstOrDefault(choice => string.Equals(
            choice.Label, _titleBlockPicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)) ??
            _titleBlockChoices[0];
        return new CreationDraft(
            _layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)],
            CurrentPaper(),
            displayModeId == Guid.Empty ? null : displayModeId,
            titleBlock,
            _namedViewDropDown.SelectedIndex > 0 ? _namedViewDropDown.SelectedValue?.ToString() : null);
    }

    private void ApplyLayoutToTargets()
    {
        if (_updatingEditors) return;
        var layout = _layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)];
        ApplyToTargets(draft => draft with { Layout = layout });
    }

    private void OnLayoutSelectionChanged(object? sender, EventArgs eventArgs)
    {
        UpdateLayoutSelector();
        ApplyLayoutToTargets();
    }

    private void UpdateLayoutSelector()
    {
        var choice = _layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)];
        _layoutSelectorButton.Text = $"{choice.Label}   {(_layoutGallery?.Visible == true ? "▴" : "▾")}";
    }

    private void ToggleLayoutGallery()
    {
        if (_layoutGallery?.Visible == true)
        {
            HideLayoutGallery();
            return;
        }

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
                Content = scrollable,
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
        var selectorBottomLeft = _layoutSelectorButton.PointToScreen(
            new PointF(0, _layoutSelectorButton.Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(selectorBottomLeft)) ??
                     Screen.PrimaryScreen;
        var workArea = screen.WorkingArea;
        var workLeft = (int)Math.Ceiling(workArea.Left);
        var workTop = (int)Math.Ceiling(workArea.Top);
        var workRight = (int)Math.Floor(workArea.Right);
        var workBottom = (int)Math.Floor(workArea.Bottom);
        var desiredWidth = Math.Clamp(_layoutPreviewTray.ContentWidth + FoundryTheme.Space4 + 2, 440, 780);
        var width = Math.Min(desiredWidth, Math.Max(320, workRight - workLeft - FoundryTheme.Space4 * 2));
        var height = LayoutPreviewTray.TrayHeight + FoundryTheme.Space4 + 2;
        var x = (int)Math.Round(selectorBottomLeft.X + _layoutSelectorButton.Width - width);
        x = Math.Clamp(x, workLeft + FoundryTheme.Space2, workRight - width - FoundryTheme.Space2);
        var y = (int)Math.Round(selectorBottomLeft.Y + FoundryTheme.Space1);
        if (y + height > workBottom - FoundryTheme.Space2)
        {
            var selectorTop = _layoutSelectorButton.PointToScreen(PointF.Empty).Y;
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

    private void ApplyPaperToTargets()
    {
        if (_updatingEditors || _updatingPaper) return;
        var paper = CurrentPaper();
        SyncPaperSelectors(paper);
        _layoutPreviewTray.SetPaper(paper);
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

    private void ApplyTitleBlockToTargets()
    {
        if (_updatingEditors) return;
        var titleBlock = _titleBlockChoices.FirstOrDefault(choice => string.Equals(
            choice.Label, _titleBlockPicker.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        if (titleBlock is null)
        {
            RefreshPreview();
            return;
        }
        ApplyToTargets(draft => draft with { TitleBlock = titleBlock });
    }

    private void ApplyNamedViewToTargets()
    {
        if (_updatingEditors) return;
        var namedView = _namedViewDropDown.SelectedIndex > 0
            ? _namedViewDropDown.SelectedValue?.ToString()
            : null;
        ApplyToTargets(draft => draft with { NamedView = namedView });
    }

    private void ApplyToTargets(Func<CreationDraft, CreationDraft> update)
    {
        foreach (var index in TargetDraftIndices())
            _drafts[index] = update(_drafts[index]);
        RefreshPreview();
    }

    private int[] SelectedDraftIndices() => _previewGrid.SelectedRows
        .Where(index => index >= 0 && index < _drafts.Count)
        .Distinct()
        .Order()
        .ToArray();

    private IReadOnlyList<int> TargetDraftIndices()
    {
        var selected = SelectedDraftIndices();
        return selected.Length > 0 ? selected : Enumerable.Range(0, _drafts.Count).ToArray();
    }

    private void OnPreviewSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingPreviewSelection) return;
        var selected = SelectedDraftIndices();
        UpdateSelectionHint();
        if (selected.Length > 0) LoadEditors(_drafts[selected[0]]);
    }

    private void ClearPreviewSelection()
    {
        _previewGrid.SelectedRows = [];
        UpdateSelectionHint();
    }

    private void UpdateSelectionHint()
    {
        var selectedCount = SelectedDraftIndices().Length;
        _selectionHint.Text = selectedCount == 0
            ? "No rows selected — property changes apply to all."
            : $"{selectedCount} selected — property changes apply only to selected rows.";
        _editAllButton.Visible = selectedCount > 0;
    }

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
            _displayModePicker.Text = draft.DisplayModeId is { } modeId
                ? _snapshot.DisplayModes.GetValueOrDefault(modeId) ?? InheritDisplayMode
                : InheritDisplayMode;
            _titleBlockPicker.Text = draft.TitleBlock.Label;
            _namedViewDropDown.SelectedIndex = NamedViewIndex(draft.NamedView);
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
        var names = _snapshot.NamedViews.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var index = Array.FindIndex(names, name => string.Equals(name, namedView, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? 0 : index + 1;
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
        new TitleBlockChoice(true, null, "Use layout template"),
        new TitleBlockChoice(false, null, "No title block"),
        .. snapshot.TitleBlockInstances.Values
            .Where(instance => instance.Transform is { Count: 16 })
            .OrderBy(instance => instance.InstanceDefinitionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(instance => instance.SourcePageName, StringComparer.OrdinalIgnoreCase)
            .Select(instance => new TitleBlockChoice(
                false,
                instance.InstanceObjectId,
                $"{instance.InstanceDefinitionName}  ·  {instance.SourcePageName}  ·  {instance.InstanceObjectId.ToString()[..8]}")),
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
    private sealed record TitleBlockChoice(bool UseTemplate, Guid? SourceInstanceObjectId, string Label);
    private sealed record PaperPreset(string Label, double Width, double Height, string UnitSystem);
    private sealed record CreationDraft(
        LayoutChoice Layout,
        PaperRecipe Paper,
        Guid? DisplayModeId,
        TitleBlockChoice TitleBlock,
        string? NamedView)
    {
        internal LayoutCreationSpec ToSpec() => new(
            1,
            Paper,
            Layout.BuiltInLayout,
            Layout.TemplateId,
            DisplayModeId,
            TitleBlock.UseTemplate,
            TitleBlock.SourceInstanceObjectId,
            NamedView);
    }

    private sealed record CreationPreviewRow(
        string Index,
        string Name,
        string LayoutType,
        string Paper,
        string Details,
        string DisplayMode,
        string TitleBlock);

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
                new Pen(selected ? FoundryTheme.SelectionAccent : FoundryTheme.CanvasBorder, selected ? 2 : 1),
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

        private static IReadOnlyList<RectangleF> DetailBounds(LayoutChoice choice, RectangleF page)
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

        private static string FitText(Graphics graphics, Font font, string text, float maximumWidth)
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
