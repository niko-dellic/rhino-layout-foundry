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
    private const string InheritDisplayMode = "Use layout/template setting";
    private const string InheritPageDisplayMode = "Use page display mode";
    private const string MixedDisplayMode = "Mixed";
    private const string InheritNamedView = "Use detail/template camera";
    private const string NoAppearanceState = "Inherit appearance state from folder";
    private const string InheritSheetAppearanceState = "Use sheet appearance state";
    private const int LayoutGroupRailWidth = 214;
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
    private readonly List<CreationDraft> _drafts = [];
    private readonly List<CreationPreviewRow> _visiblePreviewRows = [];
    private LayoutGroupKey? _activeGroupFilter;
    private Form? _layoutGallery;
    private Scrollable? _layoutGalleryScroll;
    private Form? _titleBlockGallery;
    private Scrollable? _titleBlockGalleryScroll;
    private readonly CancellationTokenSource _namedViewPreviewCancellation = new();
    private readonly CancellationTokenSource _draftLayoutPreviewCancellation = new();
    private readonly HashSet<NamedViewThumbnailKey> _pendingNamedViewPreviews = [];
    private bool _updatingPaper;
    private bool _updatingEditors;
    private bool _updatingPreviewSelection;
    private bool _dialogShown;
    private bool _draftLayoutPreviewInProgress;
    private bool _draftLayoutPreviewDirty;
    private long _draftLayoutPreviewVersion;
    private bool _editSheetPreviewInProgress;
    private bool _editSheetPreviewDirty;
    private bool _preserveEditSheetPreviewWhileRendering;
    private long _editSheetPreviewVersion;

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
        LayoutFoundryUiHost.BeginDraftLayoutThumbnailSession(
            snapshot.DocumentRuntimeSerialNumber);
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
            Orientation = Orientation.Vertical,
            Spacing = FoundryTheme.Space1,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        _layoutGroupChipScroll = new Scrollable
        {
            Border = BorderType.None,
            ExpandContentWidth = true,
            // Filling the viewport keeps a short list pinned to the top on
            // AppKit while still allowing a longer list to scroll vertically.
            ExpandContentHeight = true,
            Width = LayoutGroupRailWidth,
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
        cancel.Click += async (_, _) =>
        {
            cancel.Enabled = false;
            _namedViewPreviewCancellation.Cancel();
            _draftLayoutPreviewCancellation.Cancel();
            await LayoutFoundryUiHost.CompleteDraftLayoutThumbnailSessionAsync(
                _snapshot.DocumentRuntimeSerialNumber,
                restoreOriginalModifiedState: true,
                endSession: false);
            Close();
        };
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
        Closed += async (_, _) =>
        {
            try
            {
                _namedViewPreviewCancellation.Cancel();
                _draftLayoutPreviewCancellation.Cancel();
                CloseLayoutGallery();
                CloseTitleBlockGallery();
                _namedViewPreviewTray.DisposePreviews();
                _layoutSelectorPreview.DisposePagePreview();
                _namedViewPreviewCancellation.Dispose();
                _draftLayoutPreviewCancellation.Dispose();
            }
            finally
            {
                await LayoutFoundryUiHost.CompleteDraftLayoutThumbnailSessionAsync(
                    _snapshot.DocumentRuntimeSerialNumber,
                    restoreOriginalModifiedState: !Succeeded,
                    endSession: false);
            }
        };
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
                        _clearSelectionButton,
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
        void FitSettingsContentToViewport()
        {
            var viewportWidth = settingsPane.ClientSize.Width;
            if (viewportWidth <= 1) return;
            settingsContent.MinimumSize = new Size(viewportWidth, 0);
            settingsContent.Width = viewportWidth;
            settingsContent.Invalidate();
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
        var tablePane = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Spacing = FoundryTheme.Space2,
            Items =
            {
                new StackLayout
                {
                    Width = LayoutGroupRailWidth,
                    Spacing = FoundryTheme.Space1,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
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
                    },
                },
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
            if (_isEditMode && eventArgs.Item is CreationPreviewRow row &&
                PreviewPropertyForColumn(grid, eventArgs.Column) is { } property &&
                row.ChangedProperties.HasFlag(property))
            {
                eventArgs.BackgroundColor = FoundryTheme.WarningSurface;
                eventArgs.ForegroundColor = FoundryTheme.WarningAccent;
                eventArgs.Font = SystemFonts.Bold(11);
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
            _snapshot.DocumentRuntimeSerialNumber,
            _snapshot.Revision,
            _folders[Math.Max(0, _destinationDropDown.SelectedIndex)].Id,
            [],
            _patternBox.Text,
            1,
            1,
            CreationSpecs: _drafts.Select(draft => draft.ToSpec()).ToArray(),
            ProjectData: _snapshot.ProjectInfo,
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
            _snapshot.DocumentRuntimeSerialNumber,
            _snapshot.Revision,
            TargetDraftIndices().Select(index => _drafts[index].ExistingPageViewId)
                .OfType<Guid>().ToArray(),
            _renameChangeCheck.Checked == true ? _patternBox.Text : null,
            1,
            1,
            _paperChangeCheck.Checked == true ? _widthStepper.Value : null,
            _paperChangeCheck.Checked == true ? _heightStepper.Value : null,
            _paperChangeCheck.Checked == true ? Units[Math.Max(0, _unitDropDown.SelectedIndex)] : null,
            displayModeId,
            ChangeTitleBlock: _titleBlockChangeCheck.Checked == true,
            TitleBlockSourceInstanceObjectId: titleBlock.SourceInstanceObjectId,
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
            AppearanceStateId: SelectedCapabilityTemplate(
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
            var detailCount = Math.Min(sheet.Details.Count, draft.NamedViewsByDetail.Count);
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
                    sheet.Details[detailIndex].DetailViewportId,
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
            change.Template.TitleBlock?.InstanceDefinitionName ?? "None",
            CapabilityTemplateLabel(
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
                    choice.SourceInstanceObjectId == change.TitleBlockSourceInstanceObjectId)?.Label ?? "No title block"
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
                CapabilityTemplateLabel(appearanceStateId, _appearanceStateByLabel),
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
            SelectedCapabilityTemplate(_appearanceStatePicker, _appearanceStateByLabel),
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
        var modes = sheet.Details.Select(detail => detail.DisplayModeId).Distinct().ToArray();
        var pageMode = modes.Length == 1 ? modes[0] : (Guid?)null;
        var detailLayerIds = sheet.Details.Select(detail => detail.LayerId).Distinct().ToArray();
        var detailLayerId = detailLayerIds.Length == 1 ? detailLayerIds[0] : null;
        var usesDedicatedDetailLayer = detailLayerId is { } currentLayerId &&
                                       currentLayerId == _snapshot.DedicatedDetailLayerId;
        var titleBlock = sheet.TitleBlockBuiltInKind switch
        {
            BuiltInTitleBlockKind.FullWidthBottom => _titleBlockChoices[2],
            not null => _titleBlockChoices[1],
            _ => _titleBlockChoices[0],
        };
        var namedViews = sheet.Details.Select(detail =>
            sheet.DetailNamedViews.GetValueOrDefault(detail.DetailViewportId) ??
            sheet.NamingBinding?.NamedViews.GetValueOrDefault(detail.DetailViewportId)).ToArray();
        var detailDisplayModes = sheet.Details.Select(detail => pageMode == detail.DisplayModeId
            ? (Guid?)null
            : detail.DisplayModeId).ToArray();
        var detailAppearanceStates = sheet.Details.Select(detail =>
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
        if (!_dialogShown || _isEditMode || _draftLayoutPreviewCancellation.IsCancellationRequested)
            return;
        _draftLayoutPreviewVersion++;
        _draftLayoutPreviewDirty = true;
        if (_draftLayoutPreviewInProgress) return;
        _ = LoadDraftLayoutPreviewAsync();
    }

    private async Task LoadDraftLayoutPreviewAsync()
    {
        if (_draftLayoutPreviewInProgress) return;
        _draftLayoutPreviewInProgress = true;
        try
        {
            while (_draftLayoutPreviewDirty &&
                   !_draftLayoutPreviewCancellation.IsCancellationRequested)
            {
                _draftLayoutPreviewDirty = false;
                var version = _draftLayoutPreviewVersion;
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
                    new DraftLayoutThumbnailRequest(key, changes[targetIndex]),
                    _draftLayoutPreviewCancellation.Token);
                if (_draftLayoutPreviewCancellation.IsCancellationRequested) return;
                if (version != _draftLayoutPreviewVersion)
                {
                    _draftLayoutPreviewDirty = true;
                    continue;
                }
                if (!result.Succeeded)
                {
                    _layoutSelectorPreview.SetPagePreview(
                        null,
                        $"Preview unavailable: {result.Error ?? "Rhino did not return an image."}");
                    continue;
                }
                _layoutSelectorPreview.SetPagePreview(new Bitmap(result.PngBytes!), null);
            }
        }
        catch (OperationCanceledException)
        {
            // Dialog shutdown owns cancellation.
        }
        catch (Exception exception)
        {
            _layoutSelectorPreview.SetPagePreview(null, $"Preview unavailable: {exception.Message}");
        }
        finally
        {
            _draftLayoutPreviewInProgress = false;
            if (_draftLayoutPreviewDirty &&
                !_draftLayoutPreviewCancellation.IsCancellationRequested)
                _ = LoadDraftLayoutPreviewAsync();
        }
    }

    private void QueueEditSheetPreview(bool preserveCurrentPreview = false)
    {
        if (!_dialogShown || !_isEditMode || _draftLayoutPreviewCancellation.IsCancellationRequested)
            return;
        _editSheetPreviewVersion++;
        _editSheetPreviewDirty = true;
        _preserveEditSheetPreviewWhileRendering |= preserveCurrentPreview;
        if (_editSheetPreviewInProgress) return;
        _ = LoadEditSheetPreviewAsync();
    }

    private async Task LoadEditSheetPreviewAsync()
    {
        if (_editSheetPreviewInProgress) return;
        _editSheetPreviewInProgress = true;
        try
        {
            while (_editSheetPreviewDirty &&
                   !_draftLayoutPreviewCancellation.IsCancellationRequested)
            {
                _editSheetPreviewDirty = false;
                var version = _editSheetPreviewVersion;
                var preserveCurrentPreview = _preserveEditSheetPreviewWhileRendering;
                _preserveEditSheetPreviewWhileRendering = false;
                var targets = TargetDraftIndices();
                if (targets.Count != 1)
                {
                    ShowSharedEditStructure(targets);
                    continue;
                }

                var draft = _drafts[targets[0]];
                if (draft.ExistingPageViewId is not { } pageViewId ||
                    !_snapshot.Sheets.TryGetValue(pageViewId, out var sheet))
                {
                    _layoutSelectorPreview.SetPagePreview(null, "The selected sheet is unavailable.");
                    continue;
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
                    var detailAssignments = sheet.Details.Select((detail, detailIndex) =>
                        new EditDetailPreviewAssignment(
                            detail.DetailViewportId,
                            NormalizeNamedView(draft.NamedViewsByDetail[detailIndex]),
                            EffectiveDisplayMode(draft, detailIndex),
                            draft.AppearanceStatesByDetail[detailIndex])).ToArray();
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
                if (version != _editSheetPreviewVersion)
                {
                    _editSheetPreviewDirty = true;
                    continue;
                }
                if (pngBytes is not { Length: > 0 } || error is not null)
                {
                    _layoutSelectorPreview.SetPagePreview(
                        null,
                        $"Preview unavailable: {error ?? "Rhino did not return an image."}");
                    continue;
                }

                _layoutSelectorPreview.SetPagePreview(
                    new Bitmap(pngBytes),
                    null,
                    overlayDetails: false);
            }
        }
        catch (OperationCanceledException)
        {
            // Dialog shutdown owns cancellation.
        }
        catch (Exception exception)
        {
            _layoutSelectorPreview.SetPagePreview(null, $"Preview unavailable: {exception.Message}");
        }
        finally
        {
            _editSheetPreviewInProgress = false;
            if (_editSheetPreviewDirty &&
                !_draftLayoutPreviewCancellation.IsCancellationRequested)
                _ = LoadEditSheetPreviewAsync();
        }
    }

    private bool HasStagedVisualChanges(CreationDraft draft) =>
        _displayModeChangeCheck.Checked == true ||
        _appearanceStateChangeCheck.Checked == true ||
        DetailChangeCount(draft) > 0;

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

    private async Task LoadNamedViewPreviewsAsync()
    {
        foreach (var choice in _namedViewChoices.Where(choice => choice.Name is not null))
        {
            if (_namedViewPreviewCancellation.IsCancellationRequested) return;
            await LoadNamedViewPreviewAsync(choice.Name!, null);
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
        var source = SelectedCapabilityTemplate(
            _appearanceStatePicker, _appearanceStateByLabel);
        ApplyToTargets(draft => draft with { AppearanceStateId = source });
        if (_isEditMode)
            QueueEditSheetPreview(preserveCurrentPreview: true);
        else
            QueueDraftLayoutPreview();
    }

    private static Guid? SelectedCapabilityTemplate(
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

    private static Guid? EffectiveDisplayMode(CreationDraft draft, int detailIndex)
    {
        if (detailIndex < draft.DetailDisplayModesByDetail.Count &&
            draft.DetailDisplayModesByDetail[detailIndex] is { } detailModeId)
            return detailModeId;
        if (draft.PageDisplayModeId is { } pageModeId) return pageModeId;
        if (draft.Layout.Template is { } template && detailIndex < template.DetailSlots.Count)
            return template.DetailSlots[detailIndex].DisplayModeId;
        return null;
    }

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
                           detailIndex >= 0 && detailIndex < existingSheet.Details.Count
            ? existingSheet.Details[detailIndex].DetailViewportId
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
                slot.Layers,
                slot.Objects);
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

        HideLayoutGallery();
        HideTitleBlockGallery();
        _ = LoadNamedViewPreviewsAsync();
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
            mixedAppearanceStates);
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
            if (dialog.ChangeNamedView) namedViewAssignments[detailIndex] = dialog.NamedView;
            if (dialog.ChangeDisplayMode) displayModeAssignments[detailIndex] = dialog.DisplayModeId;
            if (dialog.ChangeAppearanceState)
                appearanceStateAssignments[detailIndex] = dialog.AppearanceStateId;
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

    private static string CapabilityTemplateLabel(
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
        new TitleBlockChoice(false, null, null, "None", null),
        new TitleBlockChoice(false, null, BuiltInTitleBlockKind.RightSidebar, "Right", null),
        new TitleBlockChoice(false, null, BuiltInTitleBlockKind.FullWidthBottom, "Bottom", null),
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
    private sealed record LayerChoice(Guid Id, string Label);
    private sealed record PaperPreset(string Label, double Width, double Height, string UnitSystem);
    private sealed record IndexModeChoice(string Label, NamingIndexMode Mode);
    private readonly record struct LayoutGroupKey(BuiltInLayoutKind? BuiltInLayout, Guid? TemplateId)
    {
        internal static LayoutGroupKey For(LayoutChoice layout) => layout.TemplateId is { } templateId
            ? new LayoutGroupKey(null, templateId)
            : new LayoutGroupKey(layout.BuiltInLayout, null);
    }
    private sealed record CreationDraft(
        Guid DraftId,
        Guid? ExistingPageViewId,
        LayoutChoice Layout,
        PaperRecipe Paper,
        Guid? PageDisplayModeId,
        bool UseDedicatedDetailLayer,
        Guid? DetailLayerId,
        TitleBlockChoice TitleBlock,
        IReadOnlyList<string?> NamedViewsByDetail,
        IReadOnlyList<Guid?> DetailDisplayModesByDetail,
        IReadOnlyList<Guid?> AppearanceStatesByDetail,
        Guid? AppearanceStateId,
        IReadOnlyList<string?> OriginalNamedViewsByDetail,
        IReadOnlyList<Guid?> OriginalDetailDisplayModesByDetail,
        IReadOnlyList<Guid?> OriginalAppearanceStatesByDetail)
    {
        internal LayoutCreationSpec ToSpec() => new(
            Quantity: 1,
            Paper: Paper,
            BuiltInLayout: Layout.BuiltInLayout,
            TemplateId: Layout.TemplateId,
            DetailDisplayModeId: PageDisplayModeId,
            UseTemplateTitleBlock: TitleBlock.UseTemplate,
            TitleBlockSourceInstanceObjectId: TitleBlock.SourceInstanceObjectId,
            BuiltInTitleBlock: TitleBlock.BuiltInKind,
            UseDedicatedDetailLayer: UseDedicatedDetailLayer,
            NamedViewsByDetail: NamedViewsByDetail,
            DetailDisplayModesByDetail: DetailDisplayModesByDetail,
            DetailLayerId: DetailLayerId,
            AppearanceStateId: AppearanceStateId,
            AppearanceStatesByDetail: AppearanceStatesByDetail);
    }

    private enum DetailLayerTargetMode
    {
        Dedicated,
        Active,
        Other,
    }

    private sealed record CreationPreviewRow(
        Guid DraftId,
        LayoutGroupKey GroupKey,
        string Index,
        string Name,
        string Destination,
        string LayoutType,
        string Paper,
        string Details,
        string DetailChanges,
        string DetailLayer,
        string DisplayMode,
        string TitleBlock,
        string AppearanceState,
        PreviewChangedProperty ChangedProperties = PreviewChangedProperty.None);

    [Flags]
    private enum PreviewChangedProperty
    {
        None = 0,
        Name = 1 << 0,
        Destination = 1 << 1,
        Paper = 1 << 2,
        DetailLayer = 1 << 3,
        DisplayMode = 1 << 4,
        TitleBlock = 1 << 5,
        AppearanceState = 1 << 6,
        DetailAssignments = 1 << 7,
    }

    private sealed record DetailPreviewState(
        string Label,
        string NamedViewLabel,
        string DisplayModeLabel,
        Bitmap? NamedViewPreview,
        string? PreviewMessage,
        bool HasNamedView,
        bool HasDisplayMode,
        bool NamedViewIsMixed,
        bool DisplayModeIsMixed,
        bool Changed);

    private sealed record PreviewAppearance(
        EffectiveViewportAppearance Effective,
        Guid FolderId,
        Guid? AppearanceStateId,
        Guid DetailSlotId);

    private sealed class DetailActivatedEventArgs(int index) : EventArgs
    {
        internal int Index { get; } = index;
    }

    private sealed class DetailAssignmentDialog : Dialog
    {
        private readonly NamedViewChoice[] _namedViewChoices;
        private readonly bool _mixedNamedViews;
        private readonly bool _mixedDisplayModes;
        private readonly DetailNamedViewTray _namedViewTray;
        private readonly FilteredPicker _displayModePicker;
        private readonly DetailDisplayModeChoice[] _displayModeChoices;
        private readonly bool _mixedAppearanceStates;
        private readonly FilteredPicker _appearanceStatePicker;
        private readonly DetailAppearanceStateChoice[] _appearanceStateChoices;
        private readonly Label _displayModeError;
        private readonly NamedViewPreviewTray _previewSource;

        internal DetailAssignmentDialog(
            string detailLabel,
            int targetCount,
            NamedViewChoice[] namedViewChoices,
            NamedViewPreviewTray previewSource,
            IReadOnlyDictionary<Guid, string> displayModes,
            string? namedView,
            bool mixedNamedViews,
            Guid? displayModeId,
            bool mixedDisplayModes,
            IReadOnlyDictionary<string, Guid> appearanceStates,
            Guid? appearanceStateId,
            bool mixedAppearanceStates)
        {
            _namedViewChoices = namedViewChoices;
            _mixedNamedViews = mixedNamedViews;
            _mixedDisplayModes = mixedDisplayModes;
            _mixedAppearanceStates = mixedAppearanceStates;
            _previewSource = previewSource;
            var namedViewIndex = Array.FindIndex(namedViewChoices, choice => string.Equals(
                choice.Name, namedView, StringComparison.OrdinalIgnoreCase));
            _namedViewTray = new DetailNamedViewTray(
                namedViewChoices,
                previewSource,
                mixedNamedViews,
                mixedNamedViews ? 0 : Math.Max(0, namedViewIndex));

            var orderedDisplayModes = displayModes
                .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var displayModeChoices = new List<DetailDisplayModeChoice>();
            if (mixedDisplayModes)
            {
                displayModeChoices.Add(new DetailDisplayModeChoice(MixedDisplayMode, null));
            }
            displayModeChoices.Add(new DetailDisplayModeChoice(InheritPageDisplayMode, null));
            foreach (var mode in orderedDisplayModes)
            {
                displayModeChoices.Add(new DetailDisplayModeChoice(mode.Value, mode.Key));
            }
            _displayModeChoices = displayModeChoices.ToArray();
            var initialDisplayMode = mixedDisplayModes
                ? _displayModeChoices[0]
                : _displayModeChoices.FirstOrDefault(choice => choice.Id == displayModeId) ??
                  _displayModeChoices.First(choice => choice.Label == InheritPageDisplayMode);
            _displayModePicker = new FilteredPicker(
                _displayModeChoices.Select(choice => choice.Label),
                "Search display modes");
            _displayModePicker.Width = 280;
            _displayModePicker.Text = initialDisplayMode.Label;
            _displayModeError = new Label
            {
                TextColor = FoundryTheme.DangerAccent,
                Wrap = WrapMode.Word,
                Visible = false,
            };
            _displayModePicker.ValueChanged += (_, _) =>
            {
                _displayModeError.Text = string.Empty;
                _displayModeError.Visible = false;
            };

            var appearanceStateChoices = new List<DetailAppearanceStateChoice>();
            if (mixedAppearanceStates)
                appearanceStateChoices.Add(new DetailAppearanceStateChoice(MixedDisplayMode, null));
            appearanceStateChoices.Add(new DetailAppearanceStateChoice(InheritSheetAppearanceState, null));
            appearanceStateChoices.AddRange(appearanceStates.Select(pair =>
                new DetailAppearanceStateChoice(pair.Key, pair.Value)));
            _appearanceStateChoices = appearanceStateChoices.ToArray();
            var initialAppearanceState = mixedAppearanceStates
                ? _appearanceStateChoices[0]
                : _appearanceStateChoices.FirstOrDefault(choice => choice.Id == appearanceStateId) ??
                  _appearanceStateChoices.First(choice => choice.Label == InheritSheetAppearanceState);
            _appearanceStatePicker = new FilteredPicker(
                _appearanceStateChoices.Select(choice => choice.Label),
                "Search appearance states");
            _appearanceStatePicker.Width = 280;
            _appearanceStatePicker.Text = initialAppearanceState.Label;

            Title = $"{detailLabel} settings";
            MinimumSize = new Size(610, 460);
            Resizable = true;
            Padding = new Padding(FoundryTheme.Space4);
            BackgroundColor = FoundryTheme.PanelBackground;

            var apply = new FoundryDialogButton("Apply", FoundryDialogButtonStyle.Primary);
            var cancel = new FoundryDialogButton("Cancel", FoundryDialogButtonStyle.Secondary);
            apply.Click += (_, _) =>
            {
                if (!_displayModePicker.ContainsChoice(_displayModePicker.Text))
                {
                    _displayModeError.Text = "Choose an available display mode or use the page setting.";
                    _displayModeError.Visible = true;
                    _displayModePicker.Focus();
                    return;
                }
                if (!_appearanceStatePicker.ContainsChoice(_appearanceStatePicker.Text))
                {
                    _displayModeError.Text =
                        "Choose an available appearance state or use the sheet setting.";
                    _displayModeError.Visible = true;
                    _appearanceStatePicker.Focus();
                    return;
                }

                Succeeded = true;
                Close();
            };
            cancel.Click += (_, _) => Close();
            FoundryDialogActions.Bind(this, apply, cancel);
            _previewSource.PreviewsChanged += OnPreviewsChanged;
            Closed += (_, _) => _previewSource.PreviewsChanged -= OnPreviewsChanged;

            var namedViewScroll = new Scrollable
            {
                Border = BorderType.None,
                ExpandContentWidth = false,
                ExpandContentHeight = false,
                Height = 270,
                Content = _namedViewTray,
            };
            Shown += (_, _) => Application.Instance.AsyncInvoke(() =>
            {
                var maximum = Math.Max(0, _namedViewTray.ContentHeight - namedViewScroll.Height);
                var target = Math.Clamp(
                    _namedViewTray.SelectedCenterY - namedViewScroll.Height / 2,
                    0,
                    maximum);
                namedViewScroll.ScrollPosition = new Point(0, target);
                _namedViewTray.Focus();
            });
            Content = new StackLayout
            {
                Spacing = FoundryTheme.Space3,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items =
                {
                    new Label
                    {
                        Text = "Named view",
                        Font = SystemFonts.Bold(13),
                        TextColor = FoundryTheme.PrimaryText,
                    },
                    new StackLayoutItem(namedViewScroll, true),
                    new TableLayout
                    {
                        Spacing = new Size(FoundryTheme.Space2, FoundryTheme.Space1),
                        Rows =
                        {
                            new TableRow(
                                new Label { Text = "Display mode" },
                                new TableCell(_displayModePicker, true)),
                            new TableRow(
                                new Label { Text = "Appearance state" },
                                new TableCell(_appearanceStatePicker, true)),
                        },
                    },
                    _displayModeError,
                    new TableLayout
                    {
                        Rows = { new TableRow(new TableCell(null, true), cancel, apply) },
                        Spacing = new Size(FoundryTheme.Space2, 0),
                    },
                },
            };
        }

        internal bool Succeeded { get; private set; }
        internal bool ChangeNamedView => !_mixedNamedViews || _namedViewTray.SelectedIndex > 0;
        internal string? NamedView
        {
            get
            {
                var index = _namedViewTray.SelectedIndex - (_mixedNamedViews ? 1 : 0);
                return index >= 0 && index < _namedViewChoices.Length
                    ? _namedViewChoices[index].Name
                    : null;
            }
        }
        internal bool ChangeDisplayMode => !_mixedDisplayModes ||
                                           !string.Equals(
                                               _displayModePicker.Text.Trim(),
                                               MixedDisplayMode,
                                               StringComparison.OrdinalIgnoreCase);
        internal Guid? DisplayModeId => _displayModeChoices.FirstOrDefault(choice =>
            string.Equals(
                choice.Label,
                _displayModePicker.Text.Trim(),
                StringComparison.OrdinalIgnoreCase))?.Id;
        internal bool ChangeAppearanceState => !_mixedAppearanceStates ||
                                               !string.Equals(
                                                   _appearanceStatePicker.Text.Trim(),
                                                   MixedDisplayMode,
                                                   StringComparison.OrdinalIgnoreCase);
        internal Guid? AppearanceStateId => _appearanceStateChoices.FirstOrDefault(choice =>
            string.Equals(
                choice.Label,
                _appearanceStatePicker.Text.Trim(),
                StringComparison.OrdinalIgnoreCase))?.Id;

        private void OnPreviewsChanged(object? sender, EventArgs eventArgs) => _namedViewTray.Invalidate();

        private sealed record DetailDisplayModeChoice(string Label, Guid? Id);
        private sealed record DetailAppearanceStateChoice(string Label, Guid? Id);
    }

    private sealed class DetailNamedViewTray : Drawable
    {
        private const int ColumnCount = 3;
        private const int TileWidth = 172;
        private const int TileHeight = 112;
        private const int Gap = 8;
        private const int TrayPadding = 4;
        private readonly NamedViewChoice[] _choices;
        private readonly NamedViewPreviewTray _previewSource;
        private readonly bool _hasMixedOption;
        private readonly Font _titleFont = SystemFonts.Bold(8);
        private int _selectedIndex;

        internal DetailNamedViewTray(
            NamedViewChoice[] choices,
            NamedViewPreviewTray previewSource,
            bool hasMixedOption,
            int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _previewSource = previewSource;
            _hasMixedOption = hasMixedOption;
            var choiceCount = choices.Length + (hasMixedOption ? 1 : 0);
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choiceCount - 1));
            var columns = Math.Min(ColumnCount, Math.Max(1, choiceCount));
            var rows = Math.Max(1, (choiceCount + ColumnCount - 1) / ColumnCount);
            Size = new Size(
                TrayPadding * 2 + columns * TileWidth + Math.Max(0, columns - 1) * Gap,
                TrayPadding * 2 + rows * TileHeight + Math.Max(0, rows - 1) * Gap);
            CanFocus = true;
            BackgroundColor = FoundryTheme.ContentBackground;
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal int SelectedIndex
        {
            get => _selectedIndex;
            private set
            {
                var choiceCount = _choices.Length + (_hasMixedOption ? 1 : 0);
                var next = Math.Clamp(value, 0, Math.Max(0, choiceCount - 1));
                if (_selectedIndex == next) return;
                _selectedIndex = next;
                Invalidate();
            }
        }

        internal int ContentHeight => Size.Height;
        internal int SelectedCenterY => (int)Math.Round(TileBounds(_selectedIndex).Center.Y);

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            graphics.FillRectangle(FoundryTheme.ContentBackground, eventArgs.ClipRectangle);
            var choiceCount = _choices.Length + (_hasMixedOption ? 1 : 0);
            for (var index = 0; index < choiceCount; index++)
            {
                var tile = TileBounds(index);
                var selected = index == _selectedIndex;
                graphics.FillRectangle(
                    selected ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.CanvasSurface,
                    tile);
                graphics.DrawRectangle(
                    new Pen(selected ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder, selected ? 2 : 1),
                    tile);
                if (_hasMixedOption && index == 0)
                {
                    var previewBounds = new RectangleF(tile.X + 8, tile.Y + 7, tile.Width - 16, 72);
                    graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, previewBounds);
                    DrawCentered(graphics, SystemFonts.Bold(10), FoundryTheme.MutedText,
                        MixedDisplayMode, previewBounds, previewBounds.Y + 27);
                    graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), previewBounds);
                    DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText,
                        MixedDisplayMode, tile, tile.Bottom - 22);
                    continue;
                }

                var choiceIndex = index - (_hasMixedOption ? 1 : 0);
                NamedViewPreviewTray.DrawPreview(
                    graphics,
                    _choices[choiceIndex],
                    _previewSource.PreviewAt(choiceIndex),
                    new RectangleF(tile.X + 8, tile.Y + 7, tile.Width - 16, 72));
                DrawCentered(graphics, _titleFont, FoundryTheme.PrimaryText,
                    _choices[choiceIndex].Label, tile, tile.Bottom - 22);
            }
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
            TrayPadding + index % ColumnCount * (TileWidth + Gap),
            TrayPadding + index / ColumnCount * (TileHeight + Gap),
            TileWidth,
            TileHeight);

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var column = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
            var row = (int)Math.Floor((eventArgs.Location.Y - TrayPadding) / (TileHeight + Gap));
            var index = row * ColumnCount + column;
            var choiceCount = _choices.Length + (_hasMixedOption ? 1 : 0);
            if (index < 0 || index >= choiceCount || !TileBounds(index).Contains(eventArgs.Location)) return;
            SelectedIndex = index;
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            var next = eventArgs.Key switch
            {
                Keys.Left => _selectedIndex - 1,
                Keys.Right => _selectedIndex + 1,
                Keys.Up => _selectedIndex - ColumnCount,
                Keys.Down => _selectedIndex + ColumnCount,
                Keys.Home => 0,
                Keys.End => _choices.Length + (_hasMixedOption ? 1 : 0) - 1,
                _ => _selectedIndex,
            };
            if (next == _selectedIndex) return;
            SelectedIndex = next;
            eventArgs.Handled = true;
        }
    }

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
        private readonly Dictionary<PreviewKey, Bitmap> _previews = [];
        private readonly Dictionary<PreviewKey, string> _previewFailures = [];
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
        internal event EventHandler? PreviewsChanged;
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
            return PreviewFor(name, null);
        }

        internal Bitmap? PreviewFor(
            string? name,
            Guid? displayModeId = null,
            PreviewAppearance? appearance = null) => string.IsNullOrWhiteSpace(name)
            ? null
            : _previews.GetValueOrDefault(PreviewKey.For(name, displayModeId, appearance));

        internal bool HasPreview(
            string name,
            Guid? displayModeId,
            PreviewAppearance? appearance = null) =>
            _previews.ContainsKey(PreviewKey.For(name, displayModeId, appearance));

        internal bool HasPreviewFailure(
            string name,
            Guid? displayModeId,
            PreviewAppearance? appearance = null) =>
            _previewFailures.ContainsKey(PreviewKey.For(name, displayModeId, appearance));

        internal void SetPreview(NamedViewThumbnailKey thumbnailKey, Bitmap bitmap)
        {
            var key = PreviewKey.For(thumbnailKey);
            if (_previews.Remove(key, out var previous)) previous.Dispose();
            _previewFailures.Remove(key);
            _previews[key] = bitmap;
            Invalidate();
            PreviewsChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetPreviewFailure(NamedViewThumbnailKey thumbnailKey, string error)
        {
            var key = PreviewKey.For(thumbnailKey);
            _previewFailures[key] = error;
            Invalidate();
            PreviewsChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void DisposePreviews()
        {
            foreach (var bitmap in _previews.Values) bitmap.Dispose();
            _previews.Clear();
            _previewFailures.Clear();
        }

        private readonly record struct PreviewKey(
            string Name,
            Guid? DisplayModeId,
            Guid? AppearanceStateId,
            Guid? AppearanceScopeId,
            Guid? DetailSlotId)
        {
            internal static PreviewKey For(
                string name,
                Guid? displayModeId,
                PreviewAppearance? appearance) => new(
                name.ToUpperInvariant(),
                displayModeId,
                appearance?.AppearanceStateId,
                appearance?.FolderId,
                appearance?.DetailSlotId);

            internal static PreviewKey For(NamedViewThumbnailKey key) => new(
                key.NamedViewName.ToUpperInvariant(),
                key.DisplayModeId,
                key.AppearanceStateId,
                key.AppearanceScopeId,
                key.DetailSlotId);
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
        private const int ColumnCount = 3;
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
            var columns = Math.Min(ColumnCount, Math.Max(1, choices.Length));
            var rows = Math.Max(1, (choices.Length + ColumnCount - 1) / ColumnCount);
            Size = new Size(
                TrayPadding * 2 + columns * TileWidth + Math.Max(0, columns - 1) * Gap,
                TrayPadding * 2 + rows * TileHeight + Math.Max(0, rows - 1) * Gap);
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? SelectedIndexChanged;
        internal event EventHandler? SelectionCommitted;
        internal int ContentWidth => Size.Width;
        internal int ContentHeight => Size.Height;
        internal int SelectedCenterY => (int)Math.Round(TileBounds(_selectedIndex).Center.Y);

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
            RectangleF page,
            bool showEmptyMarker = true)
        {
            graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 55),
                page.X + 2, page.Y + 3, page.Width, page.Height);
            graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, page);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), page);
            if (!choice.UseTemplate && choice.SourceInstanceObjectId is null && choice.BuiltInKind is null)
            {
                if (showEmptyMarker)
                    graphics.DrawLine(new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 145), 2),
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
                    var pen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 155), 1);
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
            graphics.DrawRectangle(new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 150), 1),
                page.X + margin, page.Y + margin, page.Width - margin * 2, page.Height - margin * 2);
            var blockWidth = page.Width * (choice.UseTemplate ? 0.42f : 0.48f);
            var blockHeight = page.Height * 0.24f;
            var block = new RectangleF(
                page.Right - margin - blockWidth,
                page.Bottom - margin - blockHeight,
                blockWidth,
                blockHeight);
            graphics.FillRectangle(FoundryTheme.ToolbarButtonBackground, block);
            graphics.DrawRectangle(new Pen(FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 155), 1), block);
            graphics.DrawLine(new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 145), 1),
                block.X + block.Width * 0.62f, block.Y, block.X + block.Width * 0.62f, block.Bottom);
            graphics.DrawLine(new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 145), 1),
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
            TrayPadding + index % ColumnCount * (TileWidth + Gap),
            TrayPadding + index / ColumnCount * (TileHeight + Gap),
            TileWidth,
            TileHeight);

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var column = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
            var row = (int)Math.Floor((eventArgs.Location.Y - TrayPadding) / (TileHeight + Gap));
            var index = row * ColumnCount + column;
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
                Keys.Up => _selectedIndex - ColumnCount,
                Keys.Down => _selectedIndex + ColumnCount,
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

    private sealed class LayoutPickerDrawable : Drawable
    {
        private readonly LayoutChoice[] _choices;
        private readonly Font _titleFont = SystemFonts.Bold();
        private readonly Font _subtitleFont = SystemFonts.Default();
        private int _selectedIndex;
        private bool _expanded;
        private bool _hovered;

        internal LayoutPickerDrawable(LayoutChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            Size = new Size(280, 32);
            MinimumSize = Size;
            BackgroundColor = Colors.Transparent;
            UpdateToolTip();
            Paint += OnPaint;
            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
            MouseDown += (_, eventArgs) =>
            {
                if (!Enabled || !eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
                Focus();
                Activated?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
            };
            KeyDown += (_, eventArgs) =>
            {
                if (!Enabled || eventArgs.Key is not (Keys.Enter or Keys.Space)) return;
                Activated?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
            };
        }

        internal event EventHandler? Activated;

        internal void SetSelection(int selectedIndex, bool expanded)
        {
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
            _expanded = expanded;
            UpdateToolTip();
            Invalidate();
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var surface = GraphicsPath.GetRoundRect(bounds, 6);
            graphics.FillPath(
                _hovered && Enabled ? FoundryTheme.CanvasSubtleSurface : FoundryTheme.ToolbarButtonBackground,
                surface);
            graphics.DrawPath(new Pen(
                HasFocus ? FoundryTheme.PrimaryText : FoundryTheme.CanvasBorder,
                HasFocus ? 2 : 1), surface);

            var (name, description) = PickerSummary();
            var textColor = Enabled ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
            var availableWidth = Math.Max(20, Width - 40);
            var title = LayoutPreviewTray.FitText(graphics, _titleFont, $"{name}:", availableWidth);
            var titleSize = graphics.MeasureString(_titleFont, title);
            graphics.DrawText(_titleFont, textColor, 10, (Height - titleSize.Height) / 2f, title);

            var descriptionX = 10 + titleSize.Width + FoundryTheme.Space1;
            var descriptionWidth = Math.Max(0, availableWidth - titleSize.Width - FoundryTheme.Space1);
            if (descriptionWidth > 8)
            {
                var fittedDescription = LayoutPreviewTray.FitText(
                    graphics,
                    _subtitleFont,
                    description,
                    descriptionWidth);
                var descriptionSize = graphics.MeasureString(_subtitleFont, fittedDescription);
                graphics.DrawText(
                    _subtitleFont,
                    Enabled ? FoundryTheme.SecondaryText : FoundryTheme.MutedText,
                    descriptionX,
                    (Height - descriptionSize.Height) / 2f,
                    fittedDescription);
            }

            var arrow = _expanded ? "▴" : "▾";
            var arrowFont = SystemFonts.Default(10);
            var arrowSize = graphics.MeasureString(arrowFont, arrow);
            graphics.DrawText(
                arrowFont,
                FoundryTheme.MutedText,
                Math.Max(16, Width - 22),
                (Height - arrowSize.Height) / 2f,
                arrow);
        }

        private (string Name, string Description) PickerSummary()
        {
            var parts = _choices[_selectedIndex].Label.Split([" — "], 2, StringSplitOptions.None);
            if (parts.Length == 1) return (parts[0], string.Empty);
            return _choices[_selectedIndex].TemplateId is not null ||
                   _choices[_selectedIndex].BuiltInLayout == BuiltInLayoutKind.Blank
                ? (parts[0], parts[1])
                : (parts[1], parts[0]);
        }

        private void UpdateToolTip()
        {
            var (name, description) = PickerSummary();
            ToolTip = string.IsNullOrWhiteSpace(description) ? name : $"{name}: {description}";
        }
    }

    private sealed class LayoutSelectionDrawable : Drawable
    {
        private readonly LayoutChoice[] _choices;
        private readonly Font _detailFont = SystemFonts.Bold(7);
        private readonly Font _detailMetaFont = SystemFonts.Default(6);
        private DetailPreviewState[] _detailStates = [];
        private int _selectedIndex;
        private int _hoveredDetailIndex = -1;
        private int _keyboardDetailIndex = -1;
        private PaperRecipe _paper = new(594, 420, "Millimeters");
        private TitleBlockChoice? _titleBlock;
        private Bitmap? _pagePreview;
        private string? _pagePreviewMessage;
        private bool _overlayPagePreviewDetails = true;

        internal LayoutSelectionDrawable(LayoutChoice[] choices, int selectedIndex)
            : base(true)
        {
            _choices = choices;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, choices.Length - 1));
            CanFocus = true;
            Size = new Size(428, 320);
            MinimumSize = new Size(180, 140);
            BackgroundColor = FoundryTheme.CanvasSurface;
            Paint += OnPaint;
            SizeChanged += (_, _) => Invalidate();
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeave += OnMouseLeave;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler<DetailActivatedEventArgs>? DetailActivated;
        internal bool HasPagePreview => _pagePreview is not null;

        internal void SetSelection(int selectedIndex)
        {
            if (_selectedIndex != selectedIndex)
            {
                _hoveredDetailIndex = -1;
                _keyboardDetailIndex = -1;
            }
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, _choices.Length - 1));
            Invalidate();
        }

        internal void SetDetailStates(IReadOnlyList<DetailPreviewState> states)
        {
            _detailStates = states.ToArray();
            if (_keyboardDetailIndex >= _detailStates.Length) _keyboardDetailIndex = -1;
            if (_hoveredDetailIndex >= _detailStates.Length) _hoveredDetailIndex = -1;
            Invalidate();
        }

        internal void SetPaper(PaperRecipe paper)
        {
            _paper = paper;
            Invalidate();
        }

        internal void SetTitleBlock(TitleBlockChoice titleBlock)
        {
            _titleBlock = titleBlock;
            Invalidate();
        }

        internal void SetPagePreview(
            Bitmap? preview,
            string? message,
            bool overlayDetails = true)
        {
            if (!ReferenceEquals(_pagePreview, preview))
                _pagePreview?.Dispose();
            _pagePreview = preview;
            _pagePreviewMessage = message;
            _overlayPagePreviewDetails = overlayDetails;
            Invalidate();
        }

        internal void DisposePagePreview()
        {
            _pagePreview?.Dispose();
            _pagePreview = null;
            _pagePreviewMessage = null;
            _overlayPagePreviewDetails = true;
        }

        private void OnPaint(object? sender, PaintEventArgs eventArgs)
        {
            var graphics = eventArgs.Graphics;
            graphics.AntiAlias = true;
            var page = PageBounds();
            if (_pagePreview is { } pagePreview)
            {
                graphics.DrawImage(pagePreview, page);
            }
            else
            {
                TitleBlockPreviewTray.DrawTitleBlock(
                    graphics,
                    _titleBlock ?? new TitleBlockChoice(false, null, null, "None", null),
                    _paper,
                    page,
                    showEmptyMarker: false);
                if (!string.IsNullOrWhiteSpace(_pagePreviewMessage))
                {
                    DrawCentered(
                        graphics,
                        _detailMetaFont,
                        FoundryTheme.MutedText,
                        _pagePreviewMessage,
                        page,
                        page.Y + Math.Max(10, (page.Height - 12) / 2));
                }
            }
            if (_pagePreview is not null && !_overlayPagePreviewDetails)
            {
                var previewDetails = PreviewDetailBounds(page);
                for (var detailIndex = 0;
                     detailIndex < previewDetails.Count && detailIndex < _detailStates.Length;
                     detailIndex++)
                {
                    var highlighted = detailIndex == _hoveredDetailIndex ||
                                      detailIndex == _keyboardDetailIndex;
                    var changed = _detailStates[detailIndex].Changed;
                    if (!highlighted && !changed) continue;
                    graphics.DrawRectangle(new Pen(
                        changed ? FoundryTheme.WarningAccent : FoundryTheme.PrimaryText,
                        2), previewDetails[detailIndex]);
                }
                if (HasFocus)
                    graphics.DrawRectangle(new Pen(FoundryTheme.PrimaryText, 2), page);
                return;
            }
            var details = PreviewDetailBounds(page);
            for (var detailIndex = 0; detailIndex < details.Count; detailIndex++)
            {
                var detail = details[detailIndex];
                var interactive = detailIndex < _detailStates.Length;
                var highlighted = interactive &&
                                  (detailIndex == _hoveredDetailIndex || detailIndex == _keyboardDetailIndex);
                var state = interactive ? _detailStates[detailIndex] : null;
                if (_pagePreview is not null)
                {
                    if (interactive)
                    {
                        graphics.FillRectangle(
                            FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 175),
                            detail.X, detail.Y, detail.Width, Math.Min(24, detail.Height));
                        if (detail.Height >= 30)
                            graphics.FillRectangle(
                                FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 165),
                                detail.X, detail.Bottom - 17, detail.Width, 17);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(_pagePreviewMessage))
                {
                    // Keep the sheet-level render status visible instead of
                    // covering it with the schematic detail surfaces.
                }
                else if (state?.NamedViewPreview is { } namedViewPreview)
                {
                    graphics.DrawImage(namedViewPreview, detail);
                    graphics.FillRectangle(
                        FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 175),
                        detail.X, detail.Y, detail.Width, Math.Min(24, detail.Height));
                    if (detail.Height >= 30)
                        graphics.FillRectangle(
                            FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 165),
                            detail.X, detail.Bottom - 17, detail.Width, 17);
                }
                else
                {
                    graphics.FillRectangle(FoundryTheme.ToolbarButtonBackground, detail);
                    if (!string.IsNullOrWhiteSpace(state?.PreviewMessage) && detail.Height >= 46)
                    {
                        var messageY = detail.Y + Math.Max(22, (detail.Height - 10) / 2);
                        DrawCentered(
                            graphics,
                            _detailMetaFont,
                            FoundryTheme.MutedText,
                            state.PreviewMessage,
                            detail,
                            messageY);
                    }
                }
                graphics.DrawRectangle(new Pen(
                    state?.Changed == true
                        ? FoundryTheme.WarningAccent
                        : highlighted
                            ? FoundryTheme.PrimaryText
                            : FoundryTheme.CanvasBorder,
                    state?.Changed == true || highlighted ? 2 : 1), detail);
                if (!interactive) continue;
                var previewState = _detailStates[detailIndex];
                if (detail.Width < 28 || detail.Height < 20)
                {
                    DrawCentered(graphics, _detailFont, FoundryTheme.PrimaryText,
                        (detailIndex + 1).ToString(), detail, detail.Y + (detail.Height - 10) / 2);
                    continue;
                }

                var badge = new RectangleF(detail.X + 4, detail.Y + 4, 14, 14);
                graphics.FillEllipse(FoundryTheme.CanvasSubtleSurface, badge);
                graphics.DrawEllipse(new Pen(FoundryTheme.CanvasBorder, 1), badge);
                DrawCentered(graphics, _detailFont, FoundryTheme.PrimaryText,
                    (detailIndex + 1).ToString(), badge, badge.Y + 2);

                DrawFittedText(
                    graphics,
                    _detailFont,
                    previewState.HasNamedView || previewState.NamedViewIsMixed
                        ? FoundryTheme.PrimaryText
                        : FoundryTheme.MutedText,
                    previewState.NamedViewLabel,
                    detail.X + 22,
                    detail.Y + 5,
                    Math.Max(8, detail.Width - 31));
                if (detail.Height >= 30)
                    DrawFittedText(
                        graphics,
                        _detailMetaFont,
                        previewState.HasDisplayMode || previewState.DisplayModeIsMixed
                            ? FoundryTheme.PrimaryText
                            : FoundryTheme.MutedText,
                        previewState.DisplayModeLabel,
                        detail.X + 5,
                        detail.Bottom - 12,
                        Math.Max(8, detail.Width - 15));
                graphics.DrawText(_detailMetaFont, FoundryTheme.MutedText,
                    detail.Right - 9, detail.Bottom - 12, "›");
            }
            if (HasFocus)
                graphics.DrawRectangle(new Pen(FoundryTheme.PrimaryText, 2), page);
        }

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var detailIndex = HitTestDetail(eventArgs.Location);
            if (detailIndex >= 0)
            {
                _keyboardDetailIndex = detailIndex;
                DetailActivated?.Invoke(this, new DetailActivatedEventArgs(detailIndex));
                eventArgs.Handled = true;
                Invalidate();
                return;
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs eventArgs)
        {
            var next = HitTestDetail(eventArgs.Location);
            if (_hoveredDetailIndex == next) return;
            _hoveredDetailIndex = next;
            ToolTip = next >= 0
                ? $"{_detailStates[next].Label}\nNamed view: {_detailStates[next].NamedViewLabel}\nDisplay mode: {_detailStates[next].DisplayModeLabel}\nSet named view and display mode."
                : "Sheet preview. Unconfigured details are labeled Set detail.";
            Invalidate();
        }

        private void OnMouseLeave(object? sender, MouseEventArgs eventArgs)
        {
            if (_hoveredDetailIndex < 0) return;
            _hoveredDetailIndex = -1;
            ToolTip = "Sheet preview. Unconfigured details are labeled Set detail.";
            Invalidate();
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key is Keys.Left or Keys.Up or Keys.Right or Keys.Down)
            {
                if (_detailStates.Length == 0) return;
                var delta = eventArgs.Key is Keys.Left or Keys.Up ? -1 : 1;
                _keyboardDetailIndex = _keyboardDetailIndex < 0
                    ? 0
                    : Math.Clamp(_keyboardDetailIndex + delta, 0, _detailStates.Length - 1);
                Invalidate();
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key == Keys.Enter && _keyboardDetailIndex >= 0)
            {
                DetailActivated?.Invoke(this, new DetailActivatedEventArgs(_keyboardDetailIndex));
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key == Keys.Escape && _keyboardDetailIndex >= 0)
            {
                _keyboardDetailIndex = -1;
                Invalidate();
                eventArgs.Handled = true;
                return;
            }
        }

        private RectangleF PageBounds()
        {
            return TitleBlockPreviewTray.PageBounds(
                _paper,
                new RectangleF(0, 0, Math.Max(1, Width), Math.Max(1, Height)));
        }

        private RectangleF DetailContentBounds(RectangleF page)
        {
            if (_titleBlock?.BuiltInKind is not { } kind) return page;
            try
            {
                var layout = AdaptiveTitleBlockLayoutSolver.Solve(kind, _paper);
                return new RectangleF(
                    page.X + (float)(layout.Content.Left / _paper.Width * page.Width),
                    page.Bottom - (float)(layout.Content.Top / _paper.Height * page.Height),
                    (float)(layout.Content.Width / _paper.Width * page.Width),
                    (float)(layout.Content.Height / _paper.Height * page.Height));
            }
            catch (Exception)
            {
                return page;
            }
        }

        private IReadOnlyList<RectangleF> PreviewDetailBounds(RectangleF page)
        {
            var choice = _choices[_selectedIndex];
            var targetContent = DetailContentBounds(page);
            if (_titleBlock?.BuiltInKind is null)
                return LayoutPreviewTray.DetailBounds(choice, targetContent);

            var sourceDetails = LayoutPreviewTray.DetailBounds(choice, page);
            if (sourceDetails.Count == 0) return sourceDetails;
            var sourceLeft = sourceDetails.Min(detail => detail.Left);
            var sourceTop = sourceDetails.Min(detail => detail.Top);
            var sourceRight = sourceDetails.Max(detail => detail.Right);
            var sourceBottom = sourceDetails.Max(detail => detail.Bottom);
            var sourceWidth = Math.Max(0.001f, sourceRight - sourceLeft);
            var sourceHeight = Math.Max(0.001f, sourceBottom - sourceTop);
            return sourceDetails.Select(detail => new RectangleF(
                targetContent.Left + (detail.Left - sourceLeft) / sourceWidth * targetContent.Width,
                targetContent.Top + (detail.Top - sourceTop) / sourceHeight * targetContent.Height,
                detail.Width / sourceWidth * targetContent.Width,
                detail.Height / sourceHeight * targetContent.Height)).ToArray();
        }

        private static void DrawCentered(
            Graphics graphics,
            Font font,
            Color color,
            string text,
            RectangleF bounds,
            float y)
        {
            var fitted = LayoutPreviewTray.FitText(graphics, font, text, Math.Max(4, bounds.Width - 4));
            var size = graphics.MeasureString(font, fitted);
            graphics.DrawText(font, color, bounds.X + Math.Max(2, (bounds.Width - size.Width) / 2), y, fitted);
        }

        private static void DrawFittedText(
            Graphics graphics,
            Font font,
            Color color,
            string text,
            float x,
            float y,
            float width) => graphics.DrawText(
            font,
            color,
            x,
            y,
            LayoutPreviewTray.FitText(graphics, font, text, width));

        private int HitTestDetail(PointF location)
        {
            if (_detailStates.Length == 0) return -1;
            var details = PreviewDetailBounds(PageBounds());
            for (var index = 0; index < Math.Min(details.Count, _detailStates.Length); index++)
                if (details[index].Contains(location)) return index;
            return -1;
        }
    }

    private sealed class LayoutPreviewTray : Drawable
    {
        private const int ColumnCount = 3;
        private const int TileWidth = 136;
        private const int TileHeight = 112;
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
            var columns = Math.Min(ColumnCount, Math.Max(1, choices.Length));
            var rows = Math.Max(1, (choices.Length + ColumnCount - 1) / ColumnCount);
            Size = new Size(
                TrayPadding * 2 + columns * TileWidth + Math.Max(0, columns - 1) * Gap,
                TrayPadding * 2 + rows * TileHeight + Math.Max(0, rows - 1) * Gap);
            Paint += OnPaint;
            MouseDown += OnMouseDown;
            KeyDown += OnKeyDown;
        }

        internal event EventHandler? SelectedIndexChanged;
        internal event EventHandler? SelectionCommitted;

        internal int ContentWidth => Size.Width;
        internal int ContentHeight => Size.Height;
        internal int SelectedCenterY => (int)Math.Round(TileBounds(_selectedIndex).Center.Y);

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
            graphics.FillRectangle(FoundryTheme.CanvasSubtleSurface, page);
            graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), page);
            foreach (var detail in DetailBounds(_choices[index], page))
            {
                graphics.FillRectangle(FoundryTheme.ToolbarButtonBackground, detail);
                graphics.DrawRectangle(new Pen(FoundryTheme.CanvasBorder, 1), detail);
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

            var shortSide = Math.Min(page.Width, page.Height);
            var margin = shortSide * 0.025f;
            var halfGap = shortSide * 0.01f;
            var left = page.Left + margin;
            var right = page.Right - margin;
            var top = page.Top + margin;
            var bottom = page.Bottom - margin;
            var midX = page.Center.X;
            var midY = page.Center.Y;
            return choice.BuiltInLayout switch
            {
                BuiltInLayoutKind.Blank => [],
                BuiltInLayoutKind.SingleDetail => [new RectangleF(left, top, right - left, bottom - top)],
                BuiltInLayoutKind.TwoDetailsHorizontal =>
                [
                    new RectangleF(left, top, right - left, midY - halfGap - top),
                    new RectangleF(left, midY + halfGap, right - left, bottom - midY - halfGap),
                ],
                BuiltInLayoutKind.TwoDetailsVertical =>
                [
                    new RectangleF(left, top, midX - halfGap - left, bottom - top),
                    new RectangleF(midX + halfGap, top, right - midX - halfGap, bottom - top),
                ],
                BuiltInLayoutKind.FourDetailsGrid =>
                [
                    new RectangleF(left, top, midX - halfGap - left, midY - halfGap - top),
                    new RectangleF(midX + halfGap, top, right - midX - halfGap, midY - halfGap - top),
                    new RectangleF(left, midY + halfGap, midX - halfGap - left, bottom - midY - halfGap),
                    new RectangleF(midX + halfGap, midY + halfGap,
                        right - midX - halfGap, bottom - midY - halfGap),
                ],
                _ => [],
            };
        }

        internal static RectangleF FromNormalized(
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
            TrayPadding + index % ColumnCount * (TileWidth + Gap),
            TrayPadding + index / ColumnCount * (TileHeight + Gap),
            TileWidth,
            TileHeight);

        private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
            Focus();
            var column = (int)Math.Floor((eventArgs.Location.X - TrayPadding) / (TileWidth + Gap));
            var row = (int)Math.Floor((eventArgs.Location.Y - TrayPadding) / (TileHeight + Gap));
            var index = row * ColumnCount + column;
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
                Keys.Up => _selectedIndex - ColumnCount,
                Keys.Down => _selectedIndex + ColumnCount,
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
