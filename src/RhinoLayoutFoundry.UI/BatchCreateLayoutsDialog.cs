using static RhinoLayoutFoundry.UI.BatchLayoutLabels;
using System.Linq.Expressions;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Diagnostics;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Naming;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class BatchCreateLayoutsDialog : Dialog
{
    private const int LayoutGroupButtonWidth = 192;
    private readonly DocumentSnapshot _snapshot;
    private readonly BatchTarget[] _editTargets;
    private readonly bool _isEditMode;
    private readonly IReadOnlyList<(Guid Id, string Label)> _folders;
    private readonly LayoutChoice[] _layoutChoices;
    private readonly TitleBlockChoice[] _titleBlockChoices;
    private readonly NamedViewChoice[] _namedViewChoices;
    private readonly LayerChoice[] _layerChoices;
    private readonly DropDown _destinationDropDown;
    private readonly DropDown _indexModeDropDown;
    private readonly NumericStepper _quantityStepper;
    private readonly TextBox _patternBox;
    private readonly FoundryToolbarIconButton _patternHelpButton;
    private readonly LayoutPreviewTray _layoutPreviewTray;
    private readonly LayoutPickerDrawable _layoutPickerTrigger;
    private readonly LayoutSelectionDrawable _layoutSelectorPreview;
    private readonly DropDown _paperPresetDropDown;
    private readonly DropDown _orientationDropDown;
    private readonly NumericStepper _widthStepper;
    private readonly NumericStepper _heightStepper;
    private readonly DropDown _unitDropDown;
    private readonly FilteredPicker _displayModePicker;
    private readonly FilteredPicker _appearanceStatePicker;
    private readonly Dictionary<string, Guid> _appearanceStateByLabel;
    private readonly FoundryTextSegmentedControl _detailLayerModeControl;
    private readonly DropDown _detailLayerDropDown;
    private readonly Panel _detailLayerPickerHost;
    private readonly FoundryCheckBox _renameChangeCheck;
    private readonly FoundryCheckBox _destinationChangeCheck;
    private readonly FoundryCheckBox _paperChangeCheck;
    private readonly FoundryCheckBox _displayModeChangeCheck;
    private readonly FoundryCheckBox _appearanceStateChangeCheck;
    private readonly FoundryCheckBox _detailLayerChangeCheck;
    private readonly FoundryCheckBox _titleBlockChangeCheck;
    private readonly FoundryCheckBox _revisionChangeCheck;
    private readonly TextArea _revisionEditor;
    private readonly TitleBlockPreviewTray _titleBlockPreviewTray;
    private readonly TitleBlockSelectionDrawable _titleBlockSelectorPreview;
    private readonly FoundryTextSegmentedControl _titleBlockModeControl;
    private readonly NamedViewPreviewTray _namedViewPreviewTray;
    private readonly StackLayout _layoutGroupChips;
    private readonly Scrollable _layoutGroupChipScroll;
    private readonly GridView _previewGrid;
    private readonly Label _countLabel;
    private readonly Label _selectionHint;
    private readonly FoundryToolbarIconButton _clearSelectionButton;
    private readonly Label _status;
    private readonly FoundryDialogButton _createButton;
    private readonly BatchLayoutSession _session = new();
    private List<CreationDraft> _drafts => _session.Drafts;
    private readonly List<CreationPreviewRow> _visiblePreviewRows = [];
    private LayoutGroupKey? _activeGroupFilter;
    private Form? _layoutGallery;
    private Scrollable? _layoutGalleryScroll;
    private Form? _titleBlockGallery;
    private Scrollable? _titleBlockGalleryScroll;
    private CancellationTokenSource _namedViewPreviewCancellation => _session.NamedViewCancellation;
    private CancellationTokenSource _draftLayoutPreviewCancellation => _session.LayoutCancellation;
    private readonly HashSet<NamedViewThumbnailKey> _pendingNamedViewPreviews = [];
    private bool _updatingPaper;
    private bool _updatingEditors;
    private bool _updatingPreviewSelection;
    private bool _dialogShown;
    private LatestPreviewScheduler _draftPreview => _session.DraftPreview;
    private LatestPreviewScheduler _editPreview => _session.EditPreview;
    private bool _preserveEditSheetPreviewWhileRendering;

    internal BatchCreateLayoutsDialog(DocumentSnapshot snapshot, Guid? preferredFolderId)
        : this(snapshot, preferredFolderId, null)
    {
    }

    internal BatchCreateLayoutsDialog(DocumentSnapshot snapshot, IReadOnlyList<BatchTarget> targets)
        : this(snapshot, null, targets)
    {
    }

    private BatchCreateLayoutsDialog(
        DocumentSnapshot snapshot,
        Guid? preferredFolderId,
        IReadOnlyList<BatchTarget>? editTargets)
    {
        _snapshot = snapshot;
        _editTargets = editTargets?.ToArray() ?? [];
        _isEditMode = _editTargets.Length > 0;
        _folders = FolderChoices(snapshot);
        _layoutChoices = LayoutChoices(snapshot);
        _titleBlockChoices = TitleBlockChoices(snapshot, _isEditMode);
        _namedViewChoices = NamedViewChoices(snapshot);
        _layerChoices = snapshot.Layers
            .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new LayerChoice(pair.Key, pair.Value))
            .ToArray();
        Title = _isEditMode ? "Edit layouts" : "Create layouts";
        MinimumSize = new Size(1080, 760);
        Resizable = true;
        Padding = new Padding(
            FoundryTheme.Space4,
            0,
            FoundryTheme.Space4,
            FoundryTheme.Space4);
        BackgroundColor = FoundryTheme.PanelBackground;

        _destinationDropDown = new DropDown { DataStore = _folders.Select(item => item.Label).ToArray() };
        _destinationDropDown.SelectedIndex = PreferredFolderIndex(preferredFolderId);
        _indexModeDropDown = new DropDown
        {
            DataStore = (_isEditMode
                ? EditIndexModes
                : CreateIndexModes).Select(item => item.Label).ToArray(),
            SelectedIndex = 0,
        };
        _quantityStepper = IntegerStepper(1, 1, 999);
        _patternBox = new TextBox
        {
            Text = _isEditMode ? string.Empty : "Page {index}",
            PlaceholderText = _isEditMode ? "Example: A-{index:000}" : string.Empty,
        };
        _patternHelpButton = new FoundryToolbarIconButton(
            FoundryViewIcons.Help(),
            "Show naming-pattern wildcards");
        _patternHelpButton.Click += (_, _) => new NamingPatternHelpDialog().ShowModal(this);
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
        _layoutPickerTrigger = new LayoutPickerDrawable(_layoutChoices, selectedIndex: 1);
        _layoutSelectorPreview = new LayoutSelectionDrawable(_layoutChoices, selectedIndex: 1);
        _layoutSelectorPreview.ToolTip =
            "Choose a detail to set its named view and display mode.";
        UpdateLayoutSelector();
        _displayModePicker = new FilteredPicker(
            new[] { InheritDisplayMode }.Concat(snapshot.DisplayModes.Values),
            "Search display modes");
        _displayModePicker.Text = InheritDisplayMode;
        _appearanceStateByLabel = AppearanceStateChoices(snapshot);
        _appearanceStatePicker = new FilteredPicker(
            new[] { NoAppearanceState }.Concat(_appearanceStateByLabel.Keys),
            "Search appearance states");
        _appearanceStatePicker.Text = NoAppearanceState;
        _destinationChangeCheck = new FoundryCheckBox("Destination");
        _renameChangeCheck = new FoundryCheckBox("Name / pattern");
        _paperChangeCheck = new FoundryCheckBox("Page size");
        _displayModeChangeCheck = new FoundryCheckBox("Sheet display mode");
        _appearanceStateChangeCheck = new FoundryCheckBox("Appearance state");
        _detailLayerChangeCheck = new FoundryCheckBox("Detail layer");
        _titleBlockChangeCheck = new FoundryCheckBox("Title block");
        _revisionChangeCheck = new FoundryCheckBox(_isEditMode
            ? _editTargets.Length == 1 ? "Replace revision schedule" : "Append revision"
            : "Add initial revisions");
        _destinationChangeCheck.Visible = _isEditMode;
        _renameChangeCheck.Visible = _isEditMode;
        _paperChangeCheck.Visible = _isEditMode;
        _displayModeChangeCheck.Visible = _isEditMode;
        _titleBlockChangeCheck.Visible = _isEditMode;
        _appearanceStateChangeCheck.Visible = _isEditMode;
        _detailLayerChangeCheck.Visible = _isEditMode;
        _revisionEditor = new TextArea
        {
            Height = 76,
            Wrap = false,
            ToolTip = "One row per line: Code | Date | Description | Issued by | Checked by",
        };
        if (_isEditMode && _editTargets.Length == 1 &&
            snapshot.Sheets.GetValueOrDefault(_editTargets[0].Key.Id)?.TitleBlockData is { } titleBlockData)
            _revisionEditor.Text = FormatRevisions(titleBlockData.Revisions);
        _detailLayerModeControl = new FoundryTextSegmentedControl(
            ["Dedicated", "Active", "Other"],
            selectedIndex: 0,
            segmentWidth: 68)
        {
            ToolTip = "Foundry tracks this layer by identity, so it can be renamed or moved in the layer hierarchy.",
        };
        _detailLayerDropDown = new DropDown
        {
            DataStore = _layerChoices.Select(choice => choice.Label).ToArray(),
            SelectedIndex = _layerChoices.Length == 0 ? -1 : 0,
        };
        _detailLayerPickerHost = new Panel
        {
            Content = new FoundryFormField(_detailLayerDropDown),
            Visible = false,
        };
        _titleBlockPreviewTray = new TitleBlockPreviewTray(_titleBlockChoices, selectedIndex: 0);
        _titleBlockSelectorPreview = new TitleBlockSelectionDrawable(_titleBlockChoices, selectedIndex: 0);
        _titleBlockModeControl = new FoundryTextSegmentedControl(
            ["None", "Right", "Bottom"],
            selectedIndex: 0,
            segmentWidth: 62);
        _layoutSelectorPreview.SetTitleBlock(_titleBlockChoices[0]);
        _namedViewPreviewTray = new NamedViewPreviewTray(_namedViewChoices, selectedIndex: 0);
        _namedViewPreviewTray.PreviewsChanged += (_, _) => RefreshDetailAssignments();
        _layoutGroupChips = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space1,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _layoutGroupChipScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = false,
            ExpandContentHeight = true,
            Height = 38,
            Content = _layoutGroupChips,
        };
        _previewGrid = CreatePreviewGrid();
        _countLabel = new Label { Font = SystemFonts.Default(11), TextColor = FoundryTheme.SecondaryText };
        _selectionHint = FoundryTheme.MutedLabel();
        _selectionHint.Font = SystemFonts.Default(11);
        _clearSelectionButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ClearSelection(),
            "Clear row selection and edit all layouts");
        _clearSelectionButton.Enabled = false;
        _clearSelectionButton.Click += (_, _) => ClearPreviewSelection();
        _status = FoundryTheme.MutedLabel();
        _status.Wrap = WrapMode.Word;
        _status.Visible = false;
        _createButton = new FoundryDialogButton(
            _isEditMode ? "Apply changes" : "Create layouts",
            FoundryDialogButtonStyle.Primary,
            118);
        var cancel = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        cancel.Click += (_, _) =>
            Close();
        _createButton.Click += async (_, _) => await CreateAsync();
        FoundryDialogActions.Bind(this, _createButton, cancel);

        if (_isEditMode)
        {
            foreach (var target in _editTargets)
                if (_snapshot.Sheets.TryGetValue(target.Key.Id, out var sheet))
                    _drafts.Add(DraftFromSheet(sheet));
        }
        else
        {
            _drafts.Add(DraftFromEditors());
        }
        if (_isEditMode && _drafts.Count > 0)
        {
            LoadEditors(_drafts[0]);
            var folderIds = _drafts.Select(draft =>
                    _snapshot.Sheets[draft.ExistingPageViewId!.Value].FolderId)
                .Distinct().ToArray();
            _destinationDropDown.SelectedIndex = folderIds.Length == 1
                ? Math.Max(0, _folders.Select((folder, index) => (folder, index))
                    .FirstOrDefault(item => item.folder.Id == folderIds[0]).index)
                : -1;
            if (_drafts.Select(draft => draft.AppearanceStateId).Distinct().Take(2).Count() > 1)
                _appearanceStatePicker.Text = MixedDisplayMode;
        }

        _destinationDropDown.SelectedIndexChanged += (_, _) => QueueRefreshPreview();
        _indexModeDropDown.SelectedIndexChanged += (_, _) => RefreshPreview();
        _quantityStepper.ValueChanged += (_, _) => ResizeDrafts();
        _widthStepper.ValueChanged += (_, _) => ApplyPaperToTargets();
        _heightStepper.ValueChanged += (_, _) => ApplyPaperToTargets();
        _unitDropDown.SelectedIndexChanged += (_, _) => ApplyPaperToTargets();
        _patternBox.TextChanged += (_, _) => RefreshPreview();
        _paperPresetDropDown.SelectedIndexChanged += (_, _) => QueueApplyPaperPreset();
        _orientationDropDown.SelectedIndexChanged += (_, _) => QueueApplyPaperPreset();
        _layoutPickerTrigger.Activated += (_, _) => ToggleLayoutGallery();
        _layoutSelectorPreview.DetailActivated += (_, eventArgs) => OpenDetailAssignmentDialog(eventArgs.Index);
        _layoutPreviewTray.SelectedIndexChanged += OnLayoutSelectionChanged;
        _layoutPreviewTray.SelectionCommitted += (_, _) => HideLayoutGallery();
        _displayModePicker.ValueChanged += (_, _) => ApplyDisplayModeToTargets();
        _appearanceStatePicker.SelectionCommitted += (_, _) => ApplyAppearanceStateToTargets();
        _revisionEditor.TextChanged += (_, _) => RefreshPreview();
        foreach (var check in new[]
                 {
                     _destinationChangeCheck, _renameChangeCheck, _paperChangeCheck,
                     _displayModeChangeCheck, _appearanceStateChangeCheck,
                     _detailLayerChangeCheck, _titleBlockChangeCheck, _revisionChangeCheck,
                 })
            check.CheckedChanged += (_, _) =>
            {
                RefreshEditControlState();
                RefreshPreview();
                if (ReferenceEquals(check, _displayModeChangeCheck) ||
                    ReferenceEquals(check, _appearanceStateChangeCheck))
                    QueueEditSheetPreview(preserveCurrentPreview: true);
            };
        _detailLayerModeControl.SelectedIndexChanged += (_, _) => ApplyDetailLayerTargetToTargets();
        _detailLayerDropDown.SelectedIndexChanged += (_, _) => ApplyDetailLayerTargetToTargets();
        _titleBlockSelectorPreview.Activated += (_, _) => ToggleTitleBlockGallery();
        _titleBlockPreviewTray.SelectedIndexChanged += OnTitleBlockSelectionChanged;
        _titleBlockPreviewTray.SelectionCommitted += (_, _) => HideTitleBlockGallery();
        _titleBlockModeControl.SelectedIndexChanged += (_, _) =>
        {
            _titleBlockPreviewTray.SelectedIndex = _titleBlockModeControl.SelectedIndex;
        };
        _previewGrid.SelectedRowsChanged += OnPreviewSelectionChanged;
        _displayModePicker.Opened += (_, _) =>
        {
            HideLayoutGallery();
            HideTitleBlockGallery();
        };
        Closed += (_, _) =>
PreviewCleanup = CleanupPreviewsAsync();
        LocationChanged += (_, _) =>
        {
            PositionLayoutGallery();
            PositionTitleBlockGallery();
        };

        Content = new StackLayout
        {
            Spacing = FoundryTheme.Space3,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(CreateLayoutsTab(), true),
                _status,
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
                        cancel,
                        _createButton,
                    },
                },
            },
        };
        Shown += (_, _) =>
        {
            _dialogShown = true;
            if (_isEditMode)
                QueueEditSheetPreview();
            else
                QueueDraftLayoutPreview();
        };
        RefreshEditControlState();
        RefreshPreview();
    }

    internal int CreatedCount { get; private set; }
    // The dialog owns cancellation and the capture barrier. Callers await this task after ShowModal.
    internal Task PreviewCleanup { get; private set; } = Task.CompletedTask;

    private async Task CleanupPreviewsAsync()
    {
        _namedViewPreviewCancellation.Cancel();
        _draftLayoutPreviewCancellation.Cancel();
        try
        {
            CloseLayoutGallery();
            CloseTitleBlockGallery();
            _namedViewPreviewTray.DisposePreviews();
            _layoutSelectorPreview.DisposePagePreview();
            _session.Dispose();
        }
        finally
        {
            await LayoutFoundryUiHost.WaitForPendingDraftCapturesAsync();
        }
    }
    internal bool Succeeded { get; private set; }

    private Control CreateLayoutsTab()
    {
        const int defaultSettingsPaneWidth = 500;
        const int minimumSettingsPaneWidth = 300;
        const int maximumSettingsPaneWidth = 680;
        const int defaultUpperPaneHeight = 360;
        const int minimumUpperPaneHeight = 220;
        const int minimumTablePaneHeight = 180;
        var settingsPaneWidth = defaultSettingsPaneWidth;
        var upperPaneHeight = defaultUpperPaneHeight;
        var settingsPaneCollapsed = false;
        var upperPaneCollapsed = false;
        var settingsContent = new FoundryAccordion(
            new FoundryAccordionItem("Batch", CreateBatchEditor(), isExpanded: true),
            new FoundryAccordionItem("Page size", CreatePaperEditor(), isExpanded: true),
            new FoundryAccordionItem("Layout", CreateLayoutEditor(), isExpanded: true));
        var settingsPane = new Scrollable
        {
            Border = BorderType.None,
            // The content width is synchronized explicitly below. Eto/AppKit's
            // automatic expansion remembers the largest measured child width,
            // which prevents fields from contracting after this pane is widened.
            ExpandContentWidth = false,
            // AppKit anchors a shorter scroll document at its lower edge.
            // Filling the viewport keeps collapsed accordion rows pinned to top.
            ExpandContentHeight = true,
            Content = settingsContent,
        };
        var previewPane = new Panel
        {
            Content = CreateSheetPreview(),
        };
        var settingsPaneHost = new Panel
        {
            Width = defaultSettingsPaneWidth,
            Content = settingsPane,
        };
        var settingsResizeHandle = new FoundryPaneResizeHandle(
            FoundryPaneResizeAxis.Horizontal,
            "settings");
        var appliedSettingsContentWidth = 0;
        void FitSettingsContentToViewport()
        {
            var viewportWidth = settingsPane.ClientSize.Width;
            if (viewportWidth <= 1 || viewportWidth == appliedSettingsContentWidth) return;
            // AppKit can notify SizeChanged again when the scroll document is
            // laid out, even when the viewport width is unchanged. Reassigning
            // MinimumSize/Width from that notification starts another layout
            // pass; cache before setting either property to break the cycle.
            appliedSettingsContentWidth = viewportWidth;
            settingsContent.MinimumSize = new Size(viewportWidth, 0);
            settingsContent.Width = viewportWidth;
        }
        settingsPane.SizeChanged += (_, _) =>
            Application.Instance.AsyncInvoke(FitSettingsContentToViewport);
        var upperPane = new Panel
        {
            Height = defaultUpperPaneHeight,
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Spacing = 0,
                Items =
                {
                    settingsPaneHost,
                    settingsResizeHandle,
                    new StackLayoutItem(previewPane, true),
                },
            },
        };
        var workspaceResizeHandle = new FoundryPaneResizeHandle(
            FoundryPaneResizeAxis.Vertical,
            "settings and preview");
        var layoutTypeRow = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            VerticalContentAlignment = VerticalAlignment.Center,
            Spacing = FoundryTheme.Space2,
            Items =
            {
                new Label
                {
                    Text = "Layout types",
                    Font = SystemFonts.Bold(9),
                    TextColor = FoundryTheme.SecondaryText,
                    TextAlignment = TextAlignment.Left,
                },
                new StackLayoutItem(_layoutGroupChipScroll, true),
                _clearSelectionButton,
            },
        };
        var tablePane = new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                layoutTypeRow,
                new StackLayoutItem(_previewGrid, true),
            },
        };
        var layoutRoot = new StackLayout
        {
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                upperPane,
                workspaceResizeHandle,
                new StackLayoutItem(tablePane, true),
            },
        };

        settingsResizeHandle.ResizeRequested += (_, eventArgs) =>
        {
            if (settingsPaneCollapsed)
            {
                settingsPaneCollapsed = false;
                settingsPaneHost.Visible = true;
                settingsResizeHandle.IsCollapsed = false;
            }

            settingsPaneWidth = Math.Clamp(
                settingsPaneWidth + eventArgs.Delta,
                minimumSettingsPaneWidth,
                maximumSettingsPaneWidth);
            settingsPaneHost.Width = settingsPaneWidth;
            Application.Instance.AsyncInvoke(FitSettingsContentToViewport);
        };
        settingsResizeHandle.CollapseToggleRequested += (_, _) =>
        {
            settingsPaneCollapsed = !settingsPaneCollapsed;
            settingsPaneHost.Visible = !settingsPaneCollapsed;
            settingsPaneHost.Width = settingsPaneCollapsed ? 1 : settingsPaneWidth;
            settingsResizeHandle.IsCollapsed = settingsPaneCollapsed;
            if (!settingsPaneCollapsed)
                Application.Instance.AsyncInvoke(FitSettingsContentToViewport);
        };
        workspaceResizeHandle.ResizeRequested += (_, eventArgs) =>
        {
            if (upperPaneCollapsed)
            {
                upperPaneCollapsed = false;
                upperPane.Visible = true;
                workspaceResizeHandle.IsCollapsed = false;
            }

            var availableHeight = layoutRoot.ClientSize.Height;
            var maximumHeight = availableHeight > 1
                ? Math.Max(minimumUpperPaneHeight, availableHeight - minimumTablePaneHeight)
                : defaultUpperPaneHeight;
            upperPaneHeight = Math.Clamp(
                upperPaneHeight + eventArgs.Delta,
                minimumUpperPaneHeight,
                maximumHeight);
            upperPane.Height = upperPaneHeight;
        };
        workspaceResizeHandle.CollapseToggleRequested += (_, _) =>
        {
            upperPaneCollapsed = !upperPaneCollapsed;
            upperPane.Visible = !upperPaneCollapsed;
            upperPane.Height = upperPaneCollapsed ? 1 : upperPaneHeight;
            workspaceResizeHandle.IsCollapsed = upperPaneCollapsed;
        };

        return layoutRoot;
    }

    private Control CreateBatchEditor()
    {
        var table = new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space2),
        };
        table.Rows.Add(new TableRow(_isEditMode
                ? _destinationChangeCheck
                : new Label { Text = "Destination" },
            new TableCell(new FoundryFormField(_destinationDropDown), true)));
        if (!_isEditMode)
            table.Rows.Add(new TableRow(new Label { Text = "Quantity" }, new FoundryFormField(_quantityStepper)));
        table.Rows.Add(new TableRow(_isEditMode
                ? _renameChangeCheck
                : new Label { Text = "Name / pattern" }, new StackLayout
                {
                    Orientation = Orientation.Horizontal,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Spacing = FoundryTheme.Space1,
                    Items =
            {
                new StackLayoutItem(new FoundryFormField(_patternBox), true),
                _patternHelpButton,
            },
                }));
        table.Rows.Add(new TableRow(new Label { Text = "Indexing" },
            new FoundryFormField(_indexModeDropDown)));
        return table;
    }

    private Control CreateRevisionEditor() => new StackLayout
    {
        Spacing = FoundryTheme.Space2,
        Items =
        {
            _revisionChangeCheck,
            FoundryTheme.MutedLabel(_isEditMode && _editTargets.Length > 1
                ? "Enter one row to append: Code | Date | Description | Issued by | Checked by"
                : "One row per line: Code | Date | Description | Issued by | Checked by"),
            new FoundryFormField(_revisionEditor),
        },
    };

    private Control CreateLayoutEditor()
    {
        var appearanceStateHelp = new FoundryToolbarIconButton(
            FoundryViewIcons.Help(),
            "About appearance states");
        appearanceStateHelp.Click += (_, _) => new FoundryHelpDialog(
                "Appearance State",
                _isEditMode
                    ? "Assigns a saved appearance state directly to the selected layouts. Choosing “Inherit appearance state from folder” removes the direct assignment."
                    : "Applies a saved appearance state independently of the page layout and title block. “Inherit appearance state from folder” uses the destination folder’s assignment.")
            .ShowModal(this);

        var sheetDisplayModeHelp = new FoundryToolbarIconButton(
            FoundryViewIcons.Help(),
            "About sheet display modes");
        sheetDisplayModeHelp.Click += (_, _) => new FoundryHelpDialog(
            "Sheet display mode",
            "Sets the default display mode for every detail. Individual details can override it; “Use layout/template setting” keeps the selected layout or template’s setting.")
            .ShowModal(this);

        var table = new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
        };
        table.Rows.Add(new TableRow(
            LayoutFieldKey("Title block", _titleBlockChangeCheck),
            new TableCell(_titleBlockModeControl, true)));
        if (!_isEditMode)
            table.Rows.Add(new TableRow(
                new Label { Text = "Detail layout" },
                new TableCell(_layoutPickerTrigger, true)));
        table.Rows.Add(new TableRow(
            LayoutFieldKey("Sheet display mode", _displayModeChangeCheck),
            new TableCell(PickerWithHelp(_displayModePicker, sheetDisplayModeHelp), true)));
        table.Rows.Add(new TableRow(
            LayoutFieldKey("Appearance state", _appearanceStateChangeCheck),
            new TableCell(PickerWithHelp(_appearanceStatePicker, appearanceStateHelp), true)));
        table.Rows.Add(new TableRow(
            LayoutFieldKey("Detail layer", _detailLayerChangeCheck),
            new TableCell(_detailLayerModeControl, true)));
        table.Rows.Add(new TableRow(
            new Panel(),
            new TableCell(_detailLayerPickerHost, true)));
        return table;
    }

    private Control CreateSheetPreview() => _layoutSelectorPreview;

    private Control CreatePaperEditor()
    {
        var editor = new StackLayout
        {
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        if (_isEditMode)
            editor.Items.Add(new TableLayout
            {
                Spacing = new Size(FoundryTheme.Space1, 0),
                Rows = { new TableRow(_paperChangeCheck,
                    new TableCell(new FoundryFormField(_paperPresetDropDown), true)) },
            });
        else
            editor.Items.Add(new FoundryFormField(_paperPresetDropDown));
        editor.Items.Add(new FoundryFormField(_orientationDropDown));
        editor.Items.Add(new TableLayout
        {
            Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
            Rows =
            {
                new TableRow(new Label { Text = "Width" }, new FoundryFormField(_widthStepper)),
                new TableRow(new Label { Text = "Height" }, new FoundryFormField(_heightStepper)),
                new TableRow(new Label { Text = "Units" }, new FoundryFormField(_unitDropDown)),
            },
        });
        return editor;
    }

    private Control LayoutFieldKey(string label, FoundryCheckBox editCheck) =>
        _isEditMode ? editCheck : new Label { Text = label };

    private GridView CreatePreviewGrid()
    {
        var grid = new GridView
        {
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            ToolTip = "Select one or more rows to edit only those layouts. Clear the selection to edit all layouts.",
        };
        FoundryTable.Configure(grid);
        grid.Columns.Add(TextColumn("#", row => row.Index, 44));
        grid.Columns.Add(TextColumn("Layout name", row => row.Name, 190, true));
        grid.Columns.Add(TextColumn("Destination", row => row.Destination, 150, true));
        grid.Columns.Add(TextColumn("Layout type", row => row.LayoutType, 190, true));
        grid.Columns.Add(TextColumn("Paper", row => row.Paper, 170));
        grid.Columns.Add(TextColumn("Details", row => row.Details, 70));
        grid.Columns.Add(TextColumn("Detail changes", row => row.DetailChanges, 110));
        grid.Columns.Add(TextColumn("Detail layer", row => row.DetailLayer, 105));
        grid.Columns.Add(TextColumn("Display mode", row => row.DisplayMode, 150, true));
        grid.Columns.Add(TextColumn("Title block", row => row.TitleBlock, 160, true));
        grid.Columns.Add(TextColumn("Appearance State", row => row.AppearanceState, 170, true));
        grid.CellFormatting += (_, eventArgs) =>
        {
            if (FoundryTable.FormatCell(eventArgs, grid.SelectedRows.Contains(eventArgs.Row)))
                return;
            if (_isEditMode && eventArgs.Item is CreationPreviewRow row &&
                PreviewPropertyForColumn(grid, eventArgs.Column) is { } property &&
                row.ChangedProperties.HasFlag(property))
            {
                eventArgs.BackgroundColor = FoundryTheme.WarningSurface;
                eventArgs.ForegroundColor = FoundryTheme.WarningAccent;
                eventArgs.Font = FoundryTheme.HierarchyTableBadgeFont;
            }
        };
        return grid;
    }

    private static PreviewChangedProperty? PreviewPropertyForColumn(GridView grid, GridColumn column)
    {
        if (ReferenceEquals(column, grid.Columns[1])) return PreviewChangedProperty.Name;
        if (ReferenceEquals(column, grid.Columns[2])) return PreviewChangedProperty.Destination;
        if (ReferenceEquals(column, grid.Columns[4])) return PreviewChangedProperty.Paper;
        if (ReferenceEquals(column, grid.Columns[6])) return PreviewChangedProperty.DetailAssignments;
        if (ReferenceEquals(column, grid.Columns[7])) return PreviewChangedProperty.DetailLayer;
        if (ReferenceEquals(column, grid.Columns[8])) return PreviewChangedProperty.DisplayMode;
        if (ReferenceEquals(column, grid.Columns[9])) return PreviewChangedProperty.TitleBlock;
        if (ReferenceEquals(column, grid.Columns[10])) return PreviewChangedProperty.AppearanceState;
        return null;
    }

    private BatchCreateSheetsRequest Request()
    {
        var revisions = ParseRevisions(out _);
        return new BatchCreateSheetsRequest(
            DocumentRuntimeSerialNumber: _snapshot.DocumentRuntimeSerialNumber,
            SourceRevision: _snapshot.Revision,
            DestinationFolderId: _folders[Math.Max(0, _destinationDropDown.SelectedIndex)].Id,
            NamingPattern: _patternBox.Text,
            Start: 1,
            Step: 1,
            CreationSpecs: _drafts.Select(draft => draft.ToSpec()).ToArray(),
            ProjectInfo: _snapshot.ProjectInfo,
            InitialRevisions: _revisionChangeCheck.Checked == true ? revisions : null,
            IndexMode: SelectedIndexMode());
    }

    private BatchUpdateSheetsRequest UpdateRequest()
    {
        var displayModeId = _displayModeChangeCheck.Checked == true
            ? DisplayModeId(_displayModePicker.Text.Trim())
            : null;
        var titleBlock = _titleBlockChoices[Math.Max(0, _titleBlockPreviewTray.SelectedIndex)];
        var revisions = ParseRevisions(out _);
        return new BatchUpdateSheetsRequest(
            DocumentRuntimeSerialNumber: _snapshot.DocumentRuntimeSerialNumber,
            SourceRevision: _snapshot.Revision,
            SheetPageViewIds: TargetDraftIndices().Select(index => _drafts[index].ExistingPageViewId)
                .OfType<Guid>().ToArray(),
            NamingPattern: _renameChangeCheck.Checked == true ? _patternBox.Text : null,
            Start: 1,
            Step: 1,
            PaperWidth: _paperChangeCheck.Checked == true ? _widthStepper.Value : null,
            PaperHeight: _paperChangeCheck.Checked == true ? _heightStepper.Value : null,
            PaperUnitSystem: _paperChangeCheck.Checked == true ? Units[Math.Max(0, _unitDropDown.SelectedIndex)] : null,
            DetailDisplayModeId: displayModeId,
            ChangeTitleBlock: _titleBlockChangeCheck.Checked == true,
            ReplaceRevisionSchedule: _revisionChangeCheck.Checked == true && _editTargets.Length == 1
                ? revisions
                : null,
            AppendRevision: _revisionChangeCheck.Checked == true && _editTargets.Length > 1
                ? revisions.FirstOrDefault()
                : null,
            BuiltInTitleBlock: titleBlock.BuiltInKind,
            IndexMode: SelectedIndexMode(),
            DestinationFolderId: _destinationChangeCheck.Checked == true &&
                                 _destinationDropDown.SelectedIndex >= 0
                ? _folders[_destinationDropDown.SelectedIndex].Id
                : null,
            ChangeAppearanceState: _appearanceStateChangeCheck.Checked == true,
            AppearanceStateId: SelectedAppearanceState(
                _appearanceStatePicker, _appearanceStateByLabel),
            ChangeDetailLayer: _detailLayerChangeCheck.Checked == true,
            UseDedicatedDetailLayer:
                _detailLayerModeControl.SelectedIndex == (int)DetailLayerTargetMode.Dedicated,
            DetailLayerId: SelectedDetailLayerId(),
            DetailUpdates: DetailUpdates());
    }

    private IReadOnlyList<BatchDetailUpdate> DetailUpdates()
    {
        var updates = new List<BatchDetailUpdate>();
        foreach (var targetIndex in TargetDraftIndices())
        {
            var draft = _drafts[targetIndex];
            if (draft.ExistingPageViewId is not { } pageViewId ||
                !_snapshot.Sheets.TryGetValue(pageViewId, out var sheet))
                continue;
            var orderedDetails = OrderedDetailsForDraft(sheet, draft);
            var detailCount = Math.Min(orderedDetails.Count, draft.NamedViewsByDetail.Count);
            for (var detailIndex = 0; detailIndex < detailCount; detailIndex++)
            {
                var namedViewChanged = NamedViewAssignmentChanged(draft, detailIndex);
                var displayModeChanged = DetailDisplayModeChanged(draft, detailIndex);
                var reapplyDisplayModeOverride = _displayModeChangeCheck.Checked == true &&
                                                 draft.DetailDisplayModesByDetail[detailIndex] is not null;
                var appearanceStateChanged = DetailAppearanceStateChanged(draft, detailIndex);
                if (!namedViewChanged && !displayModeChanged && !reapplyDisplayModeOverride &&
                    !appearanceStateChanged) continue;
                updates.Add(new BatchDetailUpdate(
                    orderedDetails[detailIndex].DetailViewportId,
                    namedViewChanged,
                    NormalizeNamedView(draft.NamedViewsByDetail[detailIndex]),
                    displayModeChanged || reapplyDisplayModeOverride,
                    EffectiveDisplayMode(draft, detailIndex),
                    appearanceStateChanged,
                    draft.AppearanceStatesByDetail[detailIndex]));
            }
        }
        return updates;
    }

    private void RefreshPreview(bool refreshDetailAssignments = true)
    {
        if (_updatingPaper || _folders.Count == 0) return;
        if (_isEditMode)
        {
            RefreshEditPreview();
            return;
        }
        var selectedDraftIds = SelectedDraftIds().ToHashSet();
        var plan = new BatchCreateSheetsPlanner().Plan(Request(), _snapshot);
        var changes = plan.Changes.OfType<CreateSheetFromTemplateChange>().ToArray();
        var allRows = changes.Select((change, index) => new CreationPreviewRow(
            _drafts[index].DraftId,
            LayoutGroupKey.For(_drafts[index].Layout),
            (index + 1).ToString(),
            change.Name,
            FolderLabel(change.DestinationFolderId),
            change.Template.Name,
            $"{change.Template.Paper.Width:0.###} × {change.Template.Paper.Height:0.###} {change.Template.Paper.UnitSystem}",
            change.Template.DetailSlots.Count.ToString(),
            "—",
            change.Template.DetailSlots.Count == 0
                ? "—"
                : change.UseDedicatedDetailLayer
                    ? ".details"
                    : change.DetailLayerId is { } detailLayerId
                        ? _snapshot.Layers.GetValueOrDefault(detailLayerId) ?? "Unavailable layer"
                        : "Active layer",
            DisplayModeSummary(change.Template),
            change.Template.TitleBlock is { } titleBlockRecipe ? AdaptiveTitleBlockLayoutSolver.Label(titleBlockRecipe.BuiltInKind) : "None",
            AppearanceStateLabel(
                _drafts[index].AppearanceStateId, _appearanceStateByLabel))).ToArray();
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
        ParseRevisions(out var revisionError);
        var diagnostics = string.Join(" ", plan.Diagnostics
            .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Where(item => item.Code != "batch.undo_unavailable")
            .Select(item => item.Message));
        SetStatus(pickerError ?? (_revisionChangeCheck.Checked == true ? revisionError : null) ?? diagnostics);
        _createButton.Text = CreatedCount == 1 ? "Create layout" : $"Create {CreatedCount} layouts";
        _createButton.Enabled = plan.CanApply && pickerError is null && revisionError is null;
    }

    private void RefreshEditPreview()
    {
        var selectedDraftIds = SelectedDraftIds().ToHashSet();
        var plan = new BatchUpdateSheetsPlanner().Plan(UpdateRequest(), _snapshot);
        var change = plan.Changes.OfType<BatchUpdateSheetsChange>().SingleOrDefault();
        var targetIds = change?.SheetPageViewIds.ToHashSet() ?? [];
        var allRows = _drafts.Select((draft, index) =>
        {
            var sheet = _snapshot.Sheets[draft.ExistingPageViewId!.Value];
            var targeted = targetIds.Contains(sheet.PageViewId);
            var paperWidth = targeted && change?.PaperWidth is { } width ? width : sheet.PageWidth;
            var paperHeight = targeted && change?.PaperHeight is { } height ? height : sheet.PageHeight;
            var paperUnit = targeted && change?.PaperUnitSystem is { } unit ? unit : sheet.PageUnitSystem;
            var mode = targeted && change?.DetailDisplayModeId is { } modeId
                ? _snapshot.DisplayModes.GetValueOrDefault(modeId) ?? "Unavailable"
                : BatchTargetDisplayMode(sheet);
            var titleBlock = targeted && change?.ChangeTitleBlock == true
                ? _titleBlockChoices.FirstOrDefault(choice =>
                    choice.BuiltInKind == change.BuiltInTitleBlock)?.Label ?? "No title block"
                : sheet.TitleBlockDefinitionName ?? "None";
            var destination = targeted && change?.DestinationFolderId is { } destinationId
                ? FolderLabel(destinationId)
                : FolderLabel(sheet.FolderId);
            var appearanceStateId = targeted && change?.ChangeAppearanceState == true
                ? change.AppearanceStateId
                : DirectAppearanceState(sheet.PageViewId);
            var detailLayer = targeted && change?.ChangeDetailLayer == true
                ? change.UseDedicatedDetailLayer
                    ? ".details"
                    : change.DetailLayerId is { } layerId
                        ? _snapshot.Layers.GetValueOrDefault(layerId) ?? "Unavailable layer"
                        : "Active layer"
                : DetailLayerSummary(sheet);
            var detailChangeCount = targeted ? DetailChangeCount(draft) : 0;
            var changedProperties = PreviewChangedProperty.None;
            if (targeted && change is not null)
            {
                if (change.NewNames.ContainsKey(sheet.PageViewId))
                    changedProperties |= PreviewChangedProperty.Name;
                if (change.DestinationFolderId is not null)
                    changedProperties |= PreviewChangedProperty.Destination;
                if (change.PaperWidth is not null || change.PaperHeight is not null ||
                    change.PaperUnitSystem is not null)
                    changedProperties |= PreviewChangedProperty.Paper;
                if (change.ChangeDetailLayer)
                    changedProperties |= PreviewChangedProperty.DetailLayer;
                if (change.DetailDisplayModeId is not null)
                    changedProperties |= PreviewChangedProperty.DisplayMode;
                if (change.ChangeTitleBlock)
                    changedProperties |= PreviewChangedProperty.TitleBlock;
                if (change.ChangeAppearanceState)
                    changedProperties |= PreviewChangedProperty.AppearanceState;
                if (detailChangeCount > 0)
                    changedProperties |= PreviewChangedProperty.DetailAssignments;
            }
            return new CreationPreviewRow(
                draft.DraftId,
                LayoutGroupKey.For(draft.Layout),
                (index + 1).ToString(),
                targeted && change is not null && change.NewNames.TryGetValue(sheet.PageViewId, out var name)
                    ? name
                    : sheet.Name,
                destination,
                draft.Layout.Label,
                $"{paperWidth:0.###} × {paperHeight:0.###} {paperUnit}",
                sheet.Details.Count.ToString(),
                detailChangeCount == 0
                    ? "—"
                    : $"{detailChangeCount} changed",
                detailLayer,
                mode,
                titleBlock,
            AppearanceStateLabel(appearanceStateId, _appearanceStateByLabel),
                changedProperties);
        }).ToArray();
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
                .Select(item => item.index).ToArray();
        }
        finally
        {
            _updatingPreviewSelection = false;
        }
        _countLabel.Text = $"Existing layouts  ·  {_drafts.Count}";
        RefreshGroupChips();
        UpdateSelectionHint();
        RefreshDetailAssignments();
        var pickerError = PickerError();
        ParseRevisions(out var revisionError);
        var diagnostics = string.Join(" ", plan.Diagnostics
            .Where(item => item.Severity == DiagnosticSeverity.Error && item.Code != "batch.no_changes")
            .Select(item => item.Message));
        SetStatus(pickerError ?? (_revisionChangeCheck.Checked == true ? revisionError : null) ?? diagnostics);
        _createButton.Text = "Apply changes";
        _createButton.Enabled = plan.CanApply && pickerError is null && revisionError is null;
    }

    private static string BatchTargetDisplayMode(SheetSnapshot sheet)
    {
        var modes = sheet.Details.Select(detail => detail.DisplayModeName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return modes.Length switch { 0 => "Rhino default", 1 => modes[0], _ => "Mixed" };
    }

    private string DetailLayerSummary(SheetSnapshot sheet)
    {
        var layerIds = sheet.Details.Select(detail => detail.LayerId).Distinct().ToArray();
        if (layerIds.Length == 0) return "—";
        if (layerIds.Length > 1) return "Mixed";
        if (layerIds[0] is not { } layerId) return "Unavailable layer";
        return layerId == _snapshot.DedicatedDetailLayerId
            ? ".details"
            : _snapshot.Layers.GetValueOrDefault(layerId) ?? "Unavailable layer";
    }

    private string FolderLabel(Guid folderId) =>
        _folders.FirstOrDefault(folder => folder.Id == folderId).Label ?? "Unavailable folder";

    private Guid? DirectAppearanceState(Guid sheetPageViewId) =>
        DirectAppearanceState(HierarchyScopeKind.Sheet, sheetPageViewId);

    private Guid? DirectAppearanceState(HierarchyScopeKind kind, Guid targetId) =>
        _snapshot.StateAssignments
        .LastOrDefault(assignment => assignment.Target ==
            new HierarchyScope(kind, targetId))?.StateId;

    private void QueueRefreshPreview()
    {
        Application.Instance.AsyncInvoke(() =>
        {
            RefreshPreview();
            if (!_isEditMode) QueueDraftLayoutPreview();
        });
    }

    private string? PickerError()
    {
        if ((!_isEditMode || _detailLayerChangeCheck.Checked == true) && TargetDraftIndices().Any(index =>
                !_drafts[index].UseDedicatedDetailLayer &&
                _drafts[index].DetailLayerId is { } layerId &&
                !_snapshot.Layers.ContainsKey(layerId)))
            return "Choose an available layer for the selected layouts.";
        if ((!_isEditMode || _detailLayerChangeCheck.Checked == true) &&
            _detailLayerModeControl.SelectedIndex == (int)DetailLayerTargetMode.Other &&
            (_detailLayerDropDown.SelectedIndex < 0 || _detailLayerDropDown.SelectedIndex >= _layerChoices.Length))
            return "Choose a layer for the selected layouts.";
        if ((!_isEditMode || _displayModeChangeCheck.Checked == true) &&
            !string.Equals(_displayModePicker.Text.Trim(), InheritDisplayMode, StringComparison.OrdinalIgnoreCase) &&
            !_snapshot.DisplayModes.Values.Any(name => string.Equals(
                name, _displayModePicker.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Choose an available Rhino display mode or use the layout/template setting.";
        if (_isEditMode && _displayModeChangeCheck.Checked == true &&
            string.Equals(_displayModePicker.Text.Trim(), InheritDisplayMode, StringComparison.OrdinalIgnoreCase))
            return "Choose a Rhino display mode to apply to the existing details.";
        if (_isEditMode && _destinationChangeCheck.Checked == true &&
            (_destinationDropDown.SelectedIndex < 0 || _destinationDropDown.SelectedIndex >= _folders.Count))
            return "Choose a destination folder to apply to the selected layouts.";
        if (_isEditMode && _appearanceStateChangeCheck.Checked == true &&
            !string.Equals(_appearanceStatePicker.Text.Trim(), NoAppearanceState, StringComparison.OrdinalIgnoreCase) &&
            !_appearanceStateByLabel.ContainsKey(_appearanceStatePicker.Text.Trim()))
            return "Choose an appearance state or inherit from the destination folder.";
        return null;
    }

    private async Task CreateAsync()
    {
        if (_isEditMode)
        {
            await ApplyEditsAsync();
            return;
        }
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

    private async Task ApplyEditsAsync()
    {
        try
        {
            _createButton.Enabled = false;
            _createButton.Text = "Applying…";
            SetStatus("Applying layout changes…");
            var result = await LayoutFoundryUiHost.BatchUpdateSheetsAsync(UpdateRequest());
            if (!result.Succeeded)
            {
                RefreshPreview();
                SetStatus(string.Join(" ", result.Diagnostics.Select(item => item.Message)));
                MessageBox.Show(this, _status.Text, "Edit layouts", MessageBoxType.Error);
                return;
            }
            Succeeded = true;
            Close();
        }
        catch (Exception exception)
        {
            RefreshPreview();
            SetStatus($"Layout update failed: {exception.Message}");
            MessageBox.Show(this, _status.Text, "Edit layouts", MessageBoxType.Error);
        }
    }

    private void SetStatus(string? message)
    {
        _status.Text = message ?? string.Empty;
        _status.Visible = !string.IsNullOrWhiteSpace(_status.Text);
    }

    private void RefreshEditControlState()
    {
        if (!_isEditMode)
        {
            _revisionEditor.Enabled = _revisionChangeCheck.Checked == true;
            return;
        }
        _destinationDropDown.Enabled = _destinationChangeCheck.Checked == true;
        var rename = _renameChangeCheck.Checked == true;
        _patternBox.Enabled = rename;
        _indexModeDropDown.Enabled = rename;
        var paper = _paperChangeCheck.Checked == true;
        _paperPresetDropDown.Enabled = paper;
        _orientationDropDown.Enabled = paper;
        _widthStepper.Enabled = paper;
        _heightStepper.Enabled = paper;
        _unitDropDown.Enabled = paper;
        _displayModePicker.Enabled = _displayModeChangeCheck.Checked == true;
        _appearanceStatePicker.Enabled = _appearanceStateChangeCheck.Checked == true;
        var detailLayer = _detailLayerChangeCheck.Checked == true;
        _detailLayerModeControl.Enabled = detailLayer;
        _detailLayerDropDown.Enabled = detailLayer;
        _titleBlockSelectorPreview.Enabled = _titleBlockChangeCheck.Checked == true;
        _titleBlockModeControl.Enabled = _titleBlockChangeCheck.Checked == true;
        _revisionEditor.Enabled = _revisionChangeCheck.Checked == true;
    }

    private IReadOnlyList<SheetRevisionRecord> ParseRevisions(out string? error)
    {
        error = null;
        var result = new List<SheetRevisionRecord>();
        foreach (var raw in _revisionEditor.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Split('|').Select(part => part.Trim()).ToArray();
            if (parts.Length is < 1 or > 5)
            {
                error = "Revision rows must contain at most five pipe-separated values.";
                return result;
            }
            Array.Resize(ref parts, 5);
            for (var index = 0; index < parts.Length; index++) parts[index] ??= string.Empty;
            result.Add(new SheetRevisionRecord(parts[0], parts[1], parts[2], parts[3], parts[4]));
        }
        if (_revisionChangeCheck.Checked != true) return result;
        if (!_isEditMode && result.Count == 0)
            error = "Enter at least one revision row or turn off initial revisions.";
        else if (_isEditMode && _editTargets.Length > 1 && result.Count != 1)
            error = "Enter exactly one revision row when editing multiple layouts.";
        return result;
    }

    private static string FormatRevisions(IEnumerable<SheetRevisionRecord> revisions) =>
        string.Join(Environment.NewLine, revisions.Select(revision => string.Join(" | ",
            revision.Code, revision.Date, revision.Description, revision.IssuedBy, revision.CheckedBy)));

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
        var layout = _layoutChoices[Math.Max(0, _layoutPreviewTray.SelectedIndex)];
        var namedViews = DefaultNamedViews(layout);
        var detailDisplayModes = DefaultDetailDisplayModes(layout);
        var detailAppearanceStates = DefaultDetailAppearanceStates(layout);
        return new CreationDraft(
            Guid.NewGuid(),
            null,
            layout,
            CurrentPaper(),
            displayModeId == Guid.Empty ? null : displayModeId,
            _detailLayerModeControl.SelectedIndex == (int)DetailLayerTargetMode.Dedicated,
            SelectedDetailLayerId(),
            titleBlock,
            namedViews,
            detailDisplayModes,
            detailAppearanceStates,
            SelectedAppearanceState(_appearanceStatePicker, _appearanceStateByLabel),
            namedViews,
            detailDisplayModes,
            detailAppearanceStates);
    }

    private CreationDraft DraftFromSheet(SheetSnapshot sheet)
    {
        var builtInLayout = ExistingSheetLayoutClassifier.Classify(sheet.Details);
        var layout = builtInLayout is { } kind
            ? _layoutChoices.First(choice =>
                choice.TemplateId is null && choice.BuiltInLayout == kind)
            : new LayoutChoice(
                $"Existing — {sheet.Details.Count} details",
                BuiltInLayoutKind.Blank,
                sheet.PageViewId,
                null);
        var orderedDetails = builtInLayout is { } layoutKind
            ? ExistingSheetLayoutClassifier.OrderForLayout(sheet.Details, layoutKind)
            : sheet.Details;
        var modes = orderedDetails.Select(detail => detail.DisplayModeId).Distinct().ToArray();
        var pageMode = modes.Length == 1 ? modes[0] : (Guid?)null;
        var detailLayerIds = orderedDetails.Select(detail => detail.LayerId).Distinct().ToArray();
        var detailLayerId = detailLayerIds.Length == 1 ? detailLayerIds[0] : null;
        var usesDedicatedDetailLayer = detailLayerId is { } currentLayerId &&
                                       currentLayerId == _snapshot.DedicatedDetailLayerId;
        var titleBlock = sheet.TitleBlockBuiltInKind switch
        {
            BuiltInTitleBlockKind.FullWidthBottom => _titleBlockChoices[2],
            not null => _titleBlockChoices[1],
            _ => _titleBlockChoices[0],
        };
        var namedViews = orderedDetails.Select(detail =>
            sheet.DetailNamedViews.GetValueOrDefault(detail.DetailViewportId) ??
            sheet.NamingBinding?.NamedViewAssignments.GetValueOrDefault(detail.DetailViewportId)).ToArray();
        var detailDisplayModes = orderedDetails.Select(detail => pageMode == detail.DisplayModeId
            ? (Guid?)null
            : detail.DisplayModeId).ToArray();
        var detailAppearanceStates = orderedDetails.Select(detail =>
            DirectAppearanceState(HierarchyScopeKind.Detail, detail.DetailViewportId)).ToArray();
        return new CreationDraft(
            Guid.NewGuid(),
            sheet.PageViewId,
            layout,
            new PaperRecipe(
                sheet.PageWidth > 0 ? sheet.PageWidth : 420,
                sheet.PageHeight > 0 ? sheet.PageHeight : 297,
                string.IsNullOrWhiteSpace(sheet.PageUnitSystem) ? "Millimeters" : sheet.PageUnitSystem),
            pageMode,
            usesDedicatedDetailLayer,
            usesDedicatedDetailLayer ? null : detailLayerId,
            titleBlock,
            namedViews,
            detailDisplayModes,
            detailAppearanceStates,
            DirectAppearanceState(sheet.PageViewId),
            namedViews,
            detailDisplayModes,
            detailAppearanceStates);
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
                DetailDisplayModesByDetail = DefaultDetailDisplayModes(layout),
                AppearanceStatesByDetail = DefaultDetailAppearanceStates(layout),
            };
        _activeGroupFilter = LayoutGroupKey.For(layout);
        RefreshPreview();
        if (!_isEditMode) QueueDraftLayoutPreview();
        if (selected.Count > 0) SelectDrafts(selected);
    }

    private void OnLayoutSelectionChanged(object? sender, EventArgs eventArgs)
    {
        UpdateLayoutSelector();
        ApplyLayoutToTargets();
    }

    private void UpdateLayoutSelector()
    {
        var selectedIndex = Math.Max(0, _layoutPreviewTray.SelectedIndex);
        var expanded = _layoutGallery?.Visible == true;
        _layoutPickerTrigger.SetSelection(selectedIndex, expanded);
        _layoutSelectorPreview.SetSelection(selectedIndex);
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
        var selectorBottomLeft = _layoutPickerTrigger.PointToScreen(
            new PointF(0, _layoutPickerTrigger.Height));
        var screen = Screen.Screens.FirstOrDefault(candidate => candidate.Bounds.Contains(selectorBottomLeft)) ??
                     Screen.PrimaryScreen;
        var workArea = screen.WorkingArea;
        var workLeft = (int)Math.Ceiling(workArea.Left);
        var workTop = (int)Math.Ceiling(workArea.Top);
        var workRight = (int)Math.Floor(workArea.Right);
        var workBottom = (int)Math.Floor(workArea.Bottom);
        // Keep a scrollbar gutter so wrapped galleries never fall back to horizontal scrolling.
        var desiredWidth = _layoutPreviewTray.ContentWidth + FoundryTheme.Space4 * 2 + 2;
        var width = Math.Min(desiredWidth, Math.Max(320, workRight - workLeft - FoundryTheme.Space4 * 2));
        var height = Math.Min(_layoutPreviewTray.ContentHeight + 58, 420);
        var x = (int)Math.Round(selectorBottomLeft.X + _layoutPickerTrigger.Width - width);
        x = Math.Clamp(x, workLeft + FoundryTheme.Space2, workRight - width - FoundryTheme.Space2);
        var y = (int)Math.Round(selectorBottomLeft.Y + FoundryTheme.Space1);
        if (y + height > workBottom - FoundryTheme.Space2)
        {
            var selectorTop = _layoutPickerTrigger.PointToScreen(PointF.Empty).Y;
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
        var viewportHeight = Math.Max(1, _layoutGallery.Height - 58);
        var maximum = Math.Max(0, _layoutPreviewTray.ContentHeight - viewportHeight);
        var target = Math.Clamp(_layoutPreviewTray.SelectedCenterY - viewportHeight / 2, 0, maximum);
        _layoutGalleryScroll.ScrollPosition = new Point(0, target);
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
        // Keep a scrollbar gutter so wrapped galleries never fall back to horizontal scrolling.
        var desiredWidth = _titleBlockPreviewTray.ContentWidth + FoundryTheme.Space4 * 2 + 2;
        var width = Math.Min(desiredWidth, Math.Max(320, right - left - FoundryTheme.Space4 * 2));
        var height = Math.Min(_titleBlockPreviewTray.ContentHeight + 58, 420);
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
        var viewportHeight = Math.Max(1, _titleBlockGallery.Height - 58);
        var maximum = Math.Max(0, _titleBlockPreviewTray.ContentHeight - viewportHeight);
        var target = Math.Clamp(_titleBlockPreviewTray.SelectedCenterY - viewportHeight / 2, 0, maximum);
        _titleBlockGalleryScroll.ScrollPosition = new Point(0, target);
    }

    private void QueueDraftLayoutPreview()
    {
        if (!_dialogShown || _isEditMode || _draftLayoutPreviewCancellation.IsCancellationRequested) return;
        _ = _draftPreview.RequestAsync(RenderDraftPreviewAsync, _draftLayoutPreviewCancellation.Token, PreviewFailed);
    }

    private void QueueEditSheetPreview(bool preserveCurrentPreview = false)
    {
        if (!_dialogShown || !_isEditMode || _draftLayoutPreviewCancellation.IsCancellationRequested) return;
        _preserveEditSheetPreviewWhileRendering |= preserveCurrentPreview;
        _ = _editPreview.RequestAsync(RenderEditPreviewAsync, _draftLayoutPreviewCancellation.Token, PreviewFailed);
    }

    private void PreviewFailed(Exception exception) =>
        _layoutSelectorPreview.SetPagePreview(null, $"Preview unavailable: {exception.Message}");

    private async Task RenderDraftPreviewAsync(long version)
    {
        var targetIndex = TargetDraftIndices().FirstOrDefault(-1);
        if (targetIndex < 0 || targetIndex >= _drafts.Count)
        {
            _layoutSelectorPreview.SetPagePreview(null, null);
            return;
        }

        var plan = new BatchCreateSheetsPlanner().Plan(Request(), _snapshot);
        var changes = plan.Changes.OfType<CreateSheetFromTemplateChange>().ToArray();
        if (!plan.CanApply || targetIndex >= changes.Length)
        {
            var message = plan.Diagnostics.FirstOrDefault(item =>
                item.Severity == DiagnosticSeverity.Error)?.Message ??
                "The draft is not ready to preview.";
            _layoutSelectorPreview.SetPagePreview(null, message);
            return;
        }

        var draft = _drafts[targetIndex];
        const int previewWidth = 640;
        var previewHeight = Math.Clamp(
            (int)Math.Round(previewWidth * draft.Paper.Height / Math.Max(0.001, draft.Paper.Width)),
            180,
            900);
        var key = new DraftLayoutThumbnailKey(
            _snapshot.DocumentRuntimeSerialNumber,
            draft.DraftId,
            previewWidth,
            previewHeight,
            version,
            PreviewBackgroundArgb(FoundryTheme.CanvasPreviewBackground));
        _layoutSelectorPreview.SetPagePreview(null, "Rendering preview…");
        var result = await LayoutFoundryUiHost.CaptureDraftLayoutThumbnailAsync(
            new DraftLayoutThumbnailRequest(
                key,
                WithActiveViewportDisplayModeFallback(changes[targetIndex])),
            _draftLayoutPreviewCancellation.Token);
        if (_draftLayoutPreviewCancellation.IsCancellationRequested) return;
        if (!_draftPreview.IsCurrent(version)) return;
        if (!result.Succeeded)
        {
            _layoutSelectorPreview.SetPagePreview(
                null,
                $"Preview unavailable: {result.Error ?? "Rhino did not return an image."}");
            return;
        }
        _layoutSelectorPreview.SetPagePreview(new Bitmap(result.PngBytes!), null);
    }

    private async Task RenderEditPreviewAsync(long version)
    {
        var preserveCurrentPreview = _preserveEditSheetPreviewWhileRendering;
        _preserveEditSheetPreviewWhileRendering = false;
        var targets = TargetDraftIndices();
        if (targets.Count != 1)
        {
            ShowSharedEditStructure(targets);
            return;
        }

        var draft = _drafts[targets[0]];
        if (draft.ExistingPageViewId is not { } pageViewId ||
            !_snapshot.Sheets.TryGetValue(pageViewId, out var sheet))
        {
            _layoutSelectorPreview.SetPagePreview(null, "The selected sheet is unavailable.");
            return;
        }

        const int previewWidth = 640;
        var previewHeight = Math.Clamp(
            (int)Math.Round(previewWidth * sheet.PageHeight / Math.Max(0.001, sheet.PageWidth)),
            180,
            900);
        if (!preserveCurrentPreview || !_layoutSelectorPreview.HasPagePreview)
            _layoutSelectorPreview.SetPagePreview(null, "Rendering sheet preview…");

        byte[]? pngBytes;
        string? error;
        if (HasStagedVisualChanges(draft))
        {
            var key = new EditSheetThumbnailKey(
                _snapshot.DocumentRuntimeSerialNumber,
                pageViewId,
                previewWidth,
                previewHeight,
                _snapshot.Revision + version,
                PreviewBackgroundArgb(FoundryTheme.CanvasPreviewBackground));
            var orderedDetails = OrderedDetailsForDraft(sheet, draft);
            var detailAssignments = orderedDetails.Select((detail, detailIndex) =>
                new EditDetailPreviewAssignment(
                    detail.DetailViewportId,
                    NormalizeNamedView(draft.NamedViewsByDetail[detailIndex]),
                    EffectiveDisplayMode(draft, detailIndex),
                    draft.AppearanceStatesByDetail[detailIndex],
                    NamedViewAssignmentChanged(draft, detailIndex))).ToArray();
            var result = await LayoutFoundryUiHost.CaptureEditSheetThumbnailAsync(
                new EditSheetThumbnailRequest(
                    key,
                    PreviewFolderId(draft),
                    draft.AppearanceStateId,
                    detailAssignments),
                _draftLayoutPreviewCancellation.Token);
            pngBytes = result.PngBytes;
            error = result.Error;
        }
        else
        {
            var key = new OverviewThumbnailKey(
                _snapshot.DocumentRuntimeSerialNumber,
                pageViewId,
                previewWidth,
                previewHeight,
                _snapshot.Revision + version,
                BackgroundArgb: PreviewBackgroundArgb(FoundryTheme.CanvasPreviewBackground));
            var result = await LayoutFoundryUiHost.CaptureThumbnailAsync(
                new OverviewThumbnailRequest(key, Priority: -1),
                _draftLayoutPreviewCancellation.Token);
            pngBytes = result.PngBytes;
            error = result.Error;
        }
        if (_draftLayoutPreviewCancellation.IsCancellationRequested) return;
        if (!_editPreview.IsCurrent(version)) return;
        if (pngBytes is not { Length: > 0 } || error is not null)
        {
            _layoutSelectorPreview.SetPagePreview(
                null,
                $"Preview unavailable: {error ?? "Rhino did not return an image."}");
            return;
        }

        _layoutSelectorPreview.SetPagePreview(
            new Bitmap(pngBytes),
            null,
            overlayDetails: false);
    }

    private bool HasStagedVisualChanges(CreationDraft draft) =>
        _displayModeChangeCheck.Checked == true ||
        _appearanceStateChangeCheck.Checked == true ||
        DetailChangeCount(draft) > 0;

    private CreateSheetFromTemplateChange WithActiveViewportDisplayModeFallback(
        CreateSheetFromTemplateChange change)
    {
        if (_snapshot.ActiveViewportDisplayModeId is not { } fallbackId ||
            change.Template.DetailSlots.All(slot => slot.DisplayModeId is not null))
            return change;
        return change with
        {
            Template = change.Template with
            {
                DetailSlots = change.Template.DetailSlots
                    .Select(slot => slot.DisplayModeId is null
                        ? slot with { DisplayModeId = fallbackId }
                        : slot)
                    .ToArray(),
            },
        };
    }

    private void ShowSharedEditStructure(IReadOnlyList<int> targets)
    {
        if (targets.Count == 0)
        {
            _layoutSelectorPreview.SetPagePreview(null, null);
            return;
        }

        var first = _drafts[targets[0]];
        var sameLayout = targets.All(index =>
            LayoutGroupKey.For(_drafts[index].Layout) == LayoutGroupKey.For(first.Layout));
        var sameTitleBlock = targets.All(index => _drafts[index].TitleBlock == first.TitleBlock);
        if (!sameLayout || !sameTitleBlock)
        {
            _layoutSelectorPreview.SetPagePreview(
                null,
                "Selected sheets use different layouts or title blocks.");
            return;
        }

        var selection = Math.Max(0, Array.IndexOf(_layoutChoices, first.Layout));
        _layoutSelectorPreview.SetSelection(selection);
        _layoutSelectorPreview.SetPaper(first.Paper);
        _layoutSelectorPreview.SetTitleBlock(first.TitleBlock);
        _layoutSelectorPreview.SetPagePreview(null, null);
    }

    private async Task LoadNamedViewPreviewsAsync(
        Guid? displayModeId = null,
        PreviewAppearance? previewAppearance = null)
    {
        foreach (var choice in _namedViewChoices.Where(choice => choice.Name is not null))
        {
            if (_namedViewPreviewCancellation.IsCancellationRequested) return;
            await LoadNamedViewPreviewAsync(choice.Name!, displayModeId, previewAppearance);
        }
    }

    private async Task LoadNamedViewPreviewAsync(
        string name,
        Guid? displayModeId,
        PreviewAppearance? previewAppearance = null)
    {
        if (_namedViewPreviewCancellation.IsCancellationRequested ||
            _namedViewPreviewTray.HasPreview(name, displayModeId, previewAppearance)) return;
        var key = new NamedViewThumbnailKey(
            _snapshot.DocumentRuntimeSerialNumber,
            name,
            320,
            200,
            _snapshot.Revision,
            displayModeId,
            previewAppearance?.AppearanceStateId,
            previewAppearance?.FolderId,
            previewAppearance?.DetailSlotId,
            PreviewBackgroundArgb(FoundryTheme.CanvasPreviewBackground));
        if (!_pendingNamedViewPreviews.Add(key)) return;
        try
        {
            var result = await LayoutFoundryUiHost.CaptureNamedViewThumbnailAsync(
                new NamedViewThumbnailRequest(key, previewAppearance?.Effective),
                _namedViewPreviewCancellation.Token);
            if (_namedViewPreviewCancellation.IsCancellationRequested) return;
            if (!result.Succeeded)
            {
                _namedViewPreviewTray.SetPreviewFailure(
                    result.Key,
                    result.Error ?? "Rhino could not capture this preview.");
                return;
            }
            if (_snapshot.DocumentRuntimeSerialNumber != result.Key.DocumentRuntimeSerialNumber ||
                !_snapshot.NamedViews.Contains(result.Key.NamedViewName)) return;
            _namedViewPreviewTray.SetPreview(result.Key, new Bitmap(result.PngBytes!));
        }
        finally
        {
            _pendingNamedViewPreviews.Remove(key);
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
        if (!_isEditMode) QueueDraftLayoutPreview();
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
        ApplyToTargets(draft => draft with { PageDisplayModeId = displayModeId });
        if (_isEditMode)
            QueueEditSheetPreview(preserveCurrentPreview: true);
        else
            QueueDraftLayoutPreview();
    }

    private void ApplyAppearanceStateToTargets()
    {
        if (_updatingEditors) return;
        var source = SelectedAppearanceState(
            _appearanceStatePicker, _appearanceStateByLabel);
        ApplyToTargets(draft => draft with { AppearanceStateId = source });
        if (_isEditMode)
            QueueEditSheetPreview(preserveCurrentPreview: true);
        else
            QueueDraftLayoutPreview();
    }

    private static Guid? SelectedAppearanceState(
        FilteredPicker picker,
        IReadOnlyDictionary<string, Guid> sourceByLabel) =>
        sourceByLabel.TryGetValue(picker.Text.Trim(), out var id) ? id : null;

    private Guid? DisplayModeId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label) ||
            string.Equals(label, InheritPageDisplayMode, StringComparison.OrdinalIgnoreCase))
            return null;
        var match = _snapshot.DisplayModes.FirstOrDefault(pair => string.Equals(
            pair.Value, label, StringComparison.OrdinalIgnoreCase));
        return match.Key == Guid.Empty ? null : match.Key;
    }

    private void ApplyDetailLayerTargetToTargets()
    {
        if (_updatingEditors) return;
        var mode = (DetailLayerTargetMode)Math.Clamp(
            _detailLayerModeControl.SelectedIndex,
            0,
            (int)DetailLayerTargetMode.Other);
        _detailLayerPickerHost.Visible = mode == DetailLayerTargetMode.Other;
        var layerId = mode == DetailLayerTargetMode.Other ? SelectedDetailLayerId() : null;
        ApplyToTargets(draft => draft with
        {
            UseDedicatedDetailLayer = mode == DetailLayerTargetMode.Dedicated,
            DetailLayerId = layerId,
        });
    }

    private Guid? SelectedDetailLayerId() =>
        _detailLayerModeControl.SelectedIndex == (int)DetailLayerTargetMode.Other &&
        _detailLayerDropDown.SelectedIndex >= 0 &&
        _detailLayerDropDown.SelectedIndex < _layerChoices.Length
            ? _layerChoices[_detailLayerDropDown.SelectedIndex].Id
            : null;

    private void ApplyTitleBlockToTargets()
    {
        if (_updatingEditors) return;
        var titleBlock = _titleBlockChoices[Math.Max(0, _titleBlockPreviewTray.SelectedIndex)];
        ApplyToTargets(draft => draft with { TitleBlock = titleBlock });
        if (!_isEditMode) QueueDraftLayoutPreview();
    }

    private void OnTitleBlockSelectionChanged(object? sender, EventArgs eventArgs)
    {
        _titleBlockModeControl.SelectedIndex = Math.Max(0, _titleBlockPreviewTray.SelectedIndex);
        UpdateTitleBlockSelector();
        ApplyTitleBlockToTargets();
    }

    private void UpdateTitleBlockSelector()
    {
        var selectedIndex = Math.Max(0, _titleBlockPreviewTray.SelectedIndex);
        _titleBlockSelectorPreview.SetSelection(selectedIndex, _titleBlockGallery?.Visible == true);
        _layoutSelectorPreview.SetTitleBlock(_titleBlockChoices[selectedIndex]);
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
        if (_isEditMode)
        {
            RefreshPreview(refreshDetailAssignments: false);
            QueueEditSheetPreview();
            return;
        }
        RefreshDetailAssignments();
    }

    private void ClearPreviewSelection()
    {
        _previewGrid.SelectedRows = [];
        UpdateSelectionHint();
        if (_isEditMode)
        {
            RefreshPreview(refreshDetailAssignments: false);
            QueueEditSheetPreview();
            return;
        }
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
            LayoutGroupButtonWidth)
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
        RefreshPreview();
        var first = TargetDraftIndices().FirstOrDefault(-1);
        if (first >= 0) LoadEditors(_drafts[first]);
        if (_isEditMode) QueueEditSheetPreview();
        else QueueDraftLayoutPreview();
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
        if (_isEditMode) QueueEditSheetPreview();
        else QueueDraftLayoutPreview();
    }

    private void RefreshDetailAssignments()
    {
        var targets = TargetDraftIndices();
        if (targets.Count == 0)
        {
            _layoutSelectorPreview.SetDetailStates([]);
            return;
        }

        var groupKeys = targets.Select(index => LayoutGroupKey.For(_drafts[index].Layout))
            .Distinct()
            .Take(2)
            .ToArray();
        if (groupKeys.Length != 1)
        {
            _layoutSelectorPreview.SetDetailStates([]);
            _layoutSelectorPreview.ToolTip =
                "Choose one layout group before editing individual detail settings.";
            return;
        }

        var layout = _drafts[targets[0]].Layout;
        var details = DetailLabels(layout);
        if (details.Count == 0)
        {
            _layoutSelectorPreview.SetDetailStates([]);
            _layoutSelectorPreview.ToolTip = "This layout creates no details.";
            return;
        }

        var states = new DetailPreviewState[details.Count];
        for (var detailIndex = 0; detailIndex < details.Count; detailIndex++)
        {
            var namedViewValues = targets.Select(index => _drafts[index].NamedViewsByDetail[detailIndex]).ToArray();
            var mixedNamedViews = namedViewValues.Skip(1).Any(value => !string.Equals(
                value, namedViewValues[0], StringComparison.OrdinalIgnoreCase));
            var displayModeValues = targets.Select(index =>
                EffectiveDisplayMode(_drafts[index], detailIndex)).ToArray();
            var mixedDisplayModes = displayModeValues.Skip(1).Any(value => value != displayModeValues[0]);
            var namedViewLabel = mixedNamedViews
                ? "Mixed views"
                : string.IsNullOrWhiteSpace(namedViewValues[0])
                    ? "Set detail"
                    : namedViewValues[0]!;
            var displayModeLabel = mixedDisplayModes
                ? "Mixed modes"
                : displayModeValues[0] is { } displayModeId
                    ? _snapshot.DisplayModes.GetValueOrDefault(displayModeId) ?? "Unavailable mode"
                    : "Rhino default";
            var changed = _isEditMode && targets.Any(index =>
                NamedViewAssignmentChanged(_drafts[index], detailIndex) ||
                DetailDisplayModeChanged(_drafts[index], detailIndex) ||
                DetailAppearanceStateChanged(_drafts[index], detailIndex));
            states[detailIndex] = new DetailPreviewState(
                details[detailIndex],
                namedViewLabel,
                displayModeLabel,
                null,
                null,
                namedViewValues.Any(value => !string.IsNullOrWhiteSpace(value)),
                displayModeValues.Any(value => value is not null),
                mixedNamedViews,
                mixedDisplayModes,
                changed);
        }

        _layoutSelectorPreview.SetDetailStates(states);
        _layoutSelectorPreview.ToolTip =
            "Choose a numbered detail to set its named view, display mode, and appearance state.";
    }

    private Guid? EffectiveDisplayMode(CreationDraft draft, int detailIndex)
    {
        if (detailIndex < draft.DetailDisplayModesByDetail.Count &&
            draft.DetailDisplayModesByDetail[detailIndex] is { } detailModeId)
            return detailModeId;
        return EffectiveSheetDisplayMode(draft, detailIndex);
    }

    private Guid? EffectiveSheetDisplayMode(CreationDraft draft, int detailIndex)
    {
        if (draft.PageDisplayModeId is { } pageModeId) return pageModeId;
        if (draft.Layout.Template is { } template && detailIndex < template.DetailSlots.Count)
            return template.DetailSlots[detailIndex].DisplayModeId ??
                   _snapshot.ActiveViewportDisplayModeId;
        return _snapshot.ActiveViewportDisplayModeId;
    }

    private static IReadOnlyList<DetailSnapshot> OrderedDetailsForDraft(
        SheetSnapshot sheet,
        CreationDraft draft) => draft.Layout.Template is null
        ? ExistingSheetLayoutClassifier.OrderForLayout(sheet.Details, draft.Layout.BuiltInLayout)
        : sheet.Details;

    private static string? NormalizeNamedView(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool NamedViewAssignmentChanged(CreationDraft draft, int detailIndex)
    {
        var current = detailIndex < draft.NamedViewsByDetail.Count
            ? NormalizeNamedView(draft.NamedViewsByDetail[detailIndex])
            : null;
        var original = detailIndex < draft.OriginalNamedViewsByDetail.Count
            ? NormalizeNamedView(draft.OriginalNamedViewsByDetail[detailIndex])
            : null;
        return !string.Equals(current, original, StringComparison.OrdinalIgnoreCase);
    }

    private static bool DetailDisplayModeChanged(CreationDraft draft, int detailIndex)
    {
        var current = detailIndex < draft.DetailDisplayModesByDetail.Count
            ? draft.DetailDisplayModesByDetail[detailIndex]
            : null;
        var original = detailIndex < draft.OriginalDetailDisplayModesByDetail.Count
            ? draft.OriginalDetailDisplayModesByDetail[detailIndex]
            : null;
        return current != original;
    }

    private static bool DetailAppearanceStateChanged(CreationDraft draft, int detailIndex)
    {
        var current = detailIndex < draft.AppearanceStatesByDetail.Count
            ? draft.AppearanceStatesByDetail[detailIndex]
            : null;
        var original = detailIndex < draft.OriginalAppearanceStatesByDetail.Count
            ? draft.OriginalAppearanceStatesByDetail[detailIndex]
            : null;
        return current != original;
    }

    private static int DetailChangeCount(CreationDraft draft)
    {
        var detailCount = Math.Max(
            draft.NamedViewsByDetail.Count,
            Math.Max(
                draft.OriginalDetailDisplayModesByDetail.Count,
                draft.OriginalAppearanceStatesByDetail.Count));
        return Enumerable.Range(0, detailCount).Count(index =>
            NamedViewAssignmentChanged(draft, index) ||
            DetailDisplayModeChanged(draft, index) ||
            DetailAppearanceStateChanged(draft, index));
    }

    private PreviewAppearance ResolvePreviewAppearance(CreationDraft draft, int detailIndex)
    {
        var folderId = PreviewFolderId(draft);
        var sheetScopeId = draft.ExistingPageViewId ?? draft.DraftId;
        var sheetScope = new HierarchyScope(HierarchyScopeKind.Sheet, sheetScopeId);
        var detailSlotId = draft.ExistingPageViewId is { } existingPageViewId &&
                           _snapshot.Sheets.TryGetValue(existingPageViewId, out var existingSheet) &&
                           detailIndex >= 0 &&
                           detailIndex < OrderedDetailsForDraft(existingSheet, draft).Count
            ? OrderedDetailsForDraft(existingSheet, draft)[detailIndex].DetailViewportId
            : draft.Layout.Template is { } template &&
                           detailIndex >= 0 && detailIndex < template.DetailSlots.Count
            ? template.DetailSlots[detailIndex].Id
            : PreviewDetailScopeId(draft.DraftId, detailIndex);
        var detailScope = new HierarchyScope(HierarchyScopeKind.Detail, detailSlotId);
        var scopes = PreviewFolderScopes(folderId)
            .Append(sheetScope)
            .Append(detailScope)
            .ToArray();
        var rules = _snapshot.AppearanceRules
            .GroupBy(item => item.Scope)
            .ToDictionary(group => group.Key, group => group.Last());
        if (draft.Layout.Template is { } layoutTemplate &&
            detailIndex >= 0 && detailIndex < layoutTemplate.DetailSlots.Count)
        {
            var slot = layoutTemplate.DetailSlots[detailIndex];
            rules[detailScope] = new HierarchyViewportRuleSet(
                detailScope,
                slot.LayerRules,
                slot.ObjectDisplayRules);
        }

        var assignments = _snapshot.StateAssignments.ToList();
        assignments.RemoveAll(item => item.Target == sheetScope || item.Target == detailScope);
        if (draft.AppearanceStateId is { } appearanceStateId)
        {
            assignments.Add(new AppearanceStateAssignment(
                Guid.NewGuid(),
                sheetScope,
                appearanceStateId));
        }
        if (detailIndex >= 0 && detailIndex < draft.AppearanceStatesByDetail.Count &&
            draft.AppearanceStatesByDetail[detailIndex] is { } detailAppearanceStateId)
        {
            assignments.Add(new AppearanceStateAssignment(
                Guid.NewGuid(),
                detailScope,
                detailAppearanceStateId));
        }
        var effective = ViewportAppearanceResolver.Resolve(
            scopes,
            rules,
            _snapshot.LayerSnapshots,
            _snapshot.ModelObjects,
            _snapshot.AppearanceStates.ToDictionary(item => item.Id),
            assignments);
        return new PreviewAppearance(
            effective,
            folderId,
            draft.AppearanceStateId,
            detailSlotId);
    }

    private Guid PreviewFolderId(CreationDraft draft)
    {
        if (_destinationDropDown.SelectedIndex >= 0 &&
            (!_isEditMode || _destinationChangeCheck.Checked == true))
            return _folders[_destinationDropDown.SelectedIndex].Id;
        if (draft.ExistingPageViewId is { } pageViewId &&
            _snapshot.Sheets.TryGetValue(pageViewId, out var sheet))
            return sheet.FolderId;
        return _snapshot.RootFolderId;
    }

    private IEnumerable<HierarchyScope> PreviewFolderScopes(Guid folderId)
    {
        var chain = new List<HierarchyScope>();
        var seen = new HashSet<Guid>();
        var current = folderId;
        while (_snapshot.Folders.TryGetValue(current, out var folder) && seen.Add(current))
        {
            chain.Add(new HierarchyScope(HierarchyScopeKind.Folder, current));
            if (folder.ParentId is not { } parent) break;
            current = parent;
        }
        chain.Reverse();
        return chain;
    }

    private static Guid PreviewDetailScopeId(Guid draftId, int detailIndex)
    {
        var bytes = draftId.ToByteArray();
        var indexBytes = BitConverter.GetBytes(detailIndex + 1);
        for (var index = 0; index < indexBytes.Length; index++)
            bytes[bytes.Length - indexBytes.Length + index] ^= indexBytes[index];
        return new Guid(bytes);
    }

    private static uint PreviewBackgroundArgb(Color color) =>
        ((uint)color.Ab << 24) |
        ((uint)color.Rb << 16) |
        ((uint)color.Gb << 8) |
        (uint)color.Bb;

    private void OpenDetailAssignmentDialog(int detailIndex)
    {
        var targets = TargetDraftIndices();
        if (targets.Count == 0) return;
        var groups = targets.Select(index => LayoutGroupKey.For(_drafts[index].Layout))
            .Distinct()
            .Take(2)
            .ToArray();
        if (groups.Length != 1)
        {
            MessageBox.Show(
                this,
                "Choose one layout group before editing individual detail settings.",
                "Detail settings",
                MessageBoxType.Information);
            return;
        }

        var detailLabels = DetailLabels(_drafts[targets[0]].Layout);
        if (detailIndex < 0 || detailIndex >= detailLabels.Count) return;
        var namedViews = targets.Select(index => _drafts[index].NamedViewsByDetail[detailIndex]).ToArray();
        var displayModes = targets.Select(index => _drafts[index].DetailDisplayModesByDetail[detailIndex]).ToArray();
        var appearanceStates = targets.Select(index =>
            _drafts[index].AppearanceStatesByDetail[detailIndex]).ToArray();
        var mixedNamedViews = namedViews.Skip(1).Any(value => !string.Equals(
            value, namedViews[0], StringComparison.OrdinalIgnoreCase));
        var mixedDisplayModes = displayModes.Skip(1).Any(value => value != displayModes[0]);
        var mixedAppearanceStates = appearanceStates.Skip(1).Any(value => value != appearanceStates[0]);
        var effectiveDisplayModes = targets.Select(index =>
            EffectiveDisplayMode(_drafts[index], detailIndex)).ToArray();
        var previewDisplayModeId = effectiveDisplayModes.Skip(1).Any(value => value != effectiveDisplayModes[0])
            ? _snapshot.ActiveViewportDisplayModeId
            : effectiveDisplayModes[0];
        var inheritedDisplayModes = targets.Select(index =>
            EffectiveSheetDisplayMode(_drafts[index], detailIndex)).ToArray();
        var inheritedDisplayModeId = inheritedDisplayModes.Skip(1).Any(value => value != inheritedDisplayModes[0])
            ? _snapshot.ActiveViewportDisplayModeId
            : inheritedDisplayModes[0];
        var previewAppearance = ResolvePreviewAppearance(_drafts[targets[0]], detailIndex);

        HideLayoutGallery();
        HideTitleBlockGallery();
        _ = LoadNamedViewPreviewsAsync(previewDisplayModeId, previewAppearance);
        var dialog = new DetailAssignmentDialog(
            detailLabels[detailIndex],
            targets.Count,
            _namedViewChoices,
            _namedViewPreviewTray,
            _snapshot.DisplayModes,
            namedViews[0],
            mixedNamedViews,
            displayModes[0],
            mixedDisplayModes,
            _appearanceStateByLabel,
            appearanceStates[0],
            mixedAppearanceStates,
            previewDisplayModeId,
            inheritedDisplayModeId,
            previewAppearance,
            LoadNamedViewPreviewsAsync,
            _isEditMode);
        dialog.ShowModal(this);
        if (!dialog.Succeeded) return;

        foreach (var targetIndex in targets)
        {
            var namedViewAssignments = _drafts[targetIndex].NamedViewsByDetail.ToArray();
            var displayModeAssignments = _drafts[targetIndex].DetailDisplayModesByDetail.ToArray();
            var appearanceStateAssignments = _drafts[targetIndex].AppearanceStatesByDetail.ToArray();
            if (detailIndex >= namedViewAssignments.Length ||
                detailIndex >= displayModeAssignments.Length ||
                detailIndex >= appearanceStateAssignments.Length) continue;
            if (dialog.RevertRequested)
            {
                namedViewAssignments[detailIndex] =
                    _drafts[targetIndex].OriginalNamedViewsByDetail[detailIndex];
                displayModeAssignments[detailIndex] =
                    _drafts[targetIndex].OriginalDetailDisplayModesByDetail[detailIndex];
                appearanceStateAssignments[detailIndex] =
                    _drafts[targetIndex].OriginalAppearanceStatesByDetail[detailIndex];
            }
            else
            {
                if (dialog.ChangeNamedView) namedViewAssignments[detailIndex] = dialog.NamedView;
                if (dialog.ChangeDisplayMode) displayModeAssignments[detailIndex] = dialog.DisplayModeId;
                if (dialog.ChangeAppearanceState)
                    appearanceStateAssignments[detailIndex] = dialog.AppearanceStateId;
            }
            _drafts[targetIndex] = _drafts[targetIndex] with
            {
                NamedViewsByDetail = namedViewAssignments,
                DetailDisplayModesByDetail = displayModeAssignments,
                AppearanceStatesByDetail = appearanceStateAssignments,
            };
        }
        RefreshPreview();
        if (_isEditMode)
            QueueEditSheetPreview(preserveCurrentPreview: true);
        else
            QueueDraftLayoutPreview();
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

    private static IReadOnlyList<Guid?> DefaultDetailDisplayModes(LayoutChoice layout) =>
        Enumerable.Repeat<Guid?>(null, DetailLabels(layout).Count).ToArray();

    private static IReadOnlyList<Guid?> DefaultDetailAppearanceStates(LayoutChoice layout) =>
        Enumerable.Repeat<Guid?>(null, DetailLabels(layout).Count).ToArray();

    private void LoadEditors(CreationDraft draft)
    {
        _updatingEditors = true;
        _updatingPaper = true;
        try
        {
            _layoutPreviewTray.SelectedIndex = Math.Max(0, Array.IndexOf(_layoutChoices, draft.Layout));
            UpdateLayoutSelector();
            _widthStepper.Value = draft.Paper.Width;
            _heightStepper.Value = draft.Paper.Height;
            _unitDropDown.SelectedIndex = UnitIndex(draft.Paper.UnitSystem);
            SyncPaperSelectors(draft.Paper);
            _layoutPreviewTray.SetPaper(draft.Paper);
            _layoutSelectorPreview.SetPaper(draft.Paper);
            _titleBlockPreviewTray.SetPaper(draft.Paper);
            _titleBlockSelectorPreview.SetPaper(draft.Paper);
            _displayModePicker.Text = draft.PageDisplayModeId is { } modeId
                ? _snapshot.DisplayModes.GetValueOrDefault(modeId) ?? InheritDisplayMode
                : InheritDisplayMode;
            _appearanceStatePicker.Text = draft.AppearanceStateId is { } appearanceStateId
                ? _appearanceStateByLabel.FirstOrDefault(pair => pair.Value == appearanceStateId).Key ??
                  NoAppearanceState
                : NoAppearanceState;
            var layerMode = draft.UseDedicatedDetailLayer
                ? DetailLayerTargetMode.Dedicated
                : draft.DetailLayerId is null
                    ? DetailLayerTargetMode.Active
                    : DetailLayerTargetMode.Other;
            _detailLayerModeControl.SelectedIndex = (int)layerMode;
            if (draft.DetailLayerId is { } detailLayerId)
                _detailLayerDropDown.SelectedIndex = Array.FindIndex(
                    _layerChoices, choice => choice.Id == detailLayerId);
            _detailLayerPickerHost.Visible = layerMode == DetailLayerTargetMode.Other;
            _titleBlockPreviewTray.SelectedIndex = Math.Max(0, Array.IndexOf(_titleBlockChoices, draft.TitleBlock));
            _titleBlockModeControl.SelectedIndex = _titleBlockPreviewTray.SelectedIndex;
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

    private NamingIndexMode SelectedIndexMode()
    {
        var choices = _isEditMode ? EditIndexModes : CreateIndexModes;
        return choices[Math.Clamp(_indexModeDropDown.SelectedIndex, 0, choices.Length - 1)].Mode;
    }

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

    private static Dictionary<string, Guid> AppearanceStateChoices(DocumentSnapshot snapshot)
    {
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
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

    private static string AppearanceStateLabel(
        Guid? registrationId,
        IReadOnlyDictionary<string, Guid> sourceByLabel)
    {
        if (registrationId is not { } id) return "None";
        return sourceByLabel.FirstOrDefault(pair => pair.Value == id).Key ?? "Missing source";
    }

    private int PreferredFolderIndex(Guid? preferredFolderId)
    {
        var match = _folders.Select((folder, index) => (folder, index))
            .FirstOrDefault(item => item.folder.Id == preferredFolderId);
        return match.folder == default ? 0 : match.index;
    }

    private static Control PickerWithHelp(
        Control picker,
        FoundryToolbarIconButton helpButton)
    {
        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Spacing = FoundryTheme.Space1,
            Items =
            {
                new StackLayoutItem(picker, true),
                helpButton,
            },
        };
    }

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
        Value = value,
        MinValue = min,
        MaxValue = max,
        DecimalPlaces = 0,
        Width = 76,
    };

    private static NumericStepper DimensionStepper(double value) => new()
    {
        Value = value,
        MinValue = 0.001,
        MaxValue = 1000000,
        DecimalPlaces = 3,
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
        new LayoutChoice("1 Detail — Single spread", BuiltInLayoutKind.SingleDetail, null, null),
        new LayoutChoice("2 Details — Horizontal", BuiltInLayoutKind.TwoDetailsHorizontal, null, null),
        new LayoutChoice("2 Details — Vertical", BuiltInLayoutKind.TwoDetailsVertical, null, null),
        new LayoutChoice("4 Details — Grid", BuiltInLayoutKind.FourDetailsGrid, null, null),
        .. snapshot.Templates
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .Select(template => new LayoutChoice(
                $"{template.Name} — Template", BuiltInLayoutKind.Blank, template.Id, template)),
    ];

    private static TitleBlockChoice[] TitleBlockChoices(DocumentSnapshot snapshot, bool editMode) =>
    [
        new TitleBlockChoice(BuiltInKind: null, Label: "None"),
        new TitleBlockChoice(BuiltInKind: BuiltInTitleBlockKind.RightSidebar, Label: "Right"),
        new TitleBlockChoice(BuiltInKind: BuiltInTitleBlockKind.FullWidthBottom, Label: "Bottom"),
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

    private static readonly string[] Units = ["Millimeters", "Centimeters", "Meters", "Inches", "Feet"];

    private static readonly IndexModeChoice[] CreateIndexModes =
    [
        new("Folder position", NamingIndexMode.FolderPosition),
        new("Folder + same prefix", NamingIndexMode.FolderSameStemPosition),
        new("Global position", NamingIndexMode.GlobalPosition),
        new("Global + same prefix", NamingIndexMode.GlobalSameStemPosition),
    ];

    private static readonly IndexModeChoice[] EditIndexModes =
    [
        new("Preserve current index", NamingIndexMode.PreserveCurrent),
        .. CreateIndexModes,
    ];

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

}
