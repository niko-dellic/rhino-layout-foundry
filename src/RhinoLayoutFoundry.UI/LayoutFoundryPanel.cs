using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;
using RhinoLayoutFoundry.Core.Persistence;

namespace RhinoLayoutFoundry.UI;

[Guid("c43e26dd-b64b-454b-8b50-a10560e5045f")]
public sealed partial class LayoutFoundryPanel : Panel
{
    private const string InternalHierarchyDragType = "application/x-layout-foundry-hierarchy";
    private readonly Label _emptyTitleLabel;
    private readonly Label _emptyDescriptionLabel;
    private readonly Label _summaryLabel;
    private readonly Label _statusLabel;
    private readonly TextBox _filterTextBox;
    private readonly DropDown _filterKindDropDown;
    private readonly FoundryToolbarIconButton _clearFilterButton;
    private readonly FoundryToolbarField _searchField;
    private readonly FoundryToolbarField _filterKindField;
    private readonly TextBox _renameTextBox;
    private readonly FoundryFormField _renameField;
    private readonly FoundryDialogButton _renameButton;
    private readonly Label _folderDraftDestinationLabel;
    private readonly TextBox _folderDraftTextBox;
    private readonly FoundryFormField _folderDraftField;
    private readonly FoundryDialogButton _folderDraftCreateButton;
    private readonly FoundryDialogButton _folderDraftCancelButton;
    private readonly Panel _folderDraftStrip;
    private readonly FoundryToolbarIconButton _manageButton;
    private readonly FoundryToolbarIconButton _createButton;
    private readonly FoundryToolbarIconButton _deleteButton;
    private readonly FoundryToolbarIconButton _projectInfoButton;
    private readonly FoundryToolbarIconButton _importButton;
    private readonly FoundryToolbarIconButton _exportButton;
    private readonly FoundryToolbarIconButton _listViewButton;
    private readonly FoundryToolbarIconButton _thumbnailViewButton;
    private readonly FoundryToolbarIconButton _canvasViewButton;
    private readonly FoundryToolbarButtonGroup _viewModeButtonGroup;
    private readonly FoundryToolbarIconButton _fullscreenButton;
    private readonly bool _usesMacSafeHierarchy = OperatingSystem.IsMacOS();
    private readonly TreeGridView _treeGrid;
    private readonly GridColumn _layoutsColumn;
    private readonly GridColumn _printColumn;
    private readonly GridColumn _templateColumn;
    private readonly GridColumn _paperColumn;
    private readonly GridColumn _detailsColumn;
    private readonly GridColumn _displayModeColumn;
    private readonly GridColumn _layersColumn;
    private readonly GridColumn _objectModesColumn;
    private readonly GridColumn _statusColumn;
    private readonly TextBoxCell _paperCell;
    private readonly CustomCell _displayModeCell;
    private readonly Panel _contentHost;
    private readonly Panel _toolbarSurface;
    private readonly Panel _renameActions;
    private readonly Control _managementView;
    private readonly ThumbnailFoundryPanel _thumbnailView;
    private readonly Control _thumbnailDensityControl;
    private readonly ObserverFoundryPanel _observerView;
    private readonly Panel _viewHost;
    private readonly Control _panelShell;
    private readonly PixelLayout _panelOverlayHost;
    private readonly DeleteConfirmationOverlay _deleteConfirmationOverlay;
    private readonly CreateResourceMenuOverlay _createMenuOverlay;
    private readonly TemplateRolesOverlay _templateRolesOverlay;
    private ButtonMenuItem _setCurrentMenuItem = null!;
    private ButtonMenuItem _newFolderMenuItem = null!;
    private ButtonMenuItem _newPageMenuItem = null!;
    private ButtonMenuItem _newLayerStateMenuItem = null!;
    private ButtonMenuItem _newObjectStateMenuItem = null!;
    private ButtonMenuItem _duplicateSelectionMenuItem = null!;
    private ButtonMenuItem _copySelectionMenuItem = null!;
    private ButtonMenuItem _pasteSelectionMenuItem = null!;
    private ButtonMenuItem _deleteSelectionMenuItem = null!;
    private ButtonMenuItem _renamePageMenuItem = null!;
    private ButtonMenuItem _newDetailMenuItem = null!;
    private ButtonMenuItem _printPageMenuItem = null!;
    private ButtonMenuItem _printScopeMenuItem = null!;
    private ButtonMenuItem _propertiesPageMenuItem = null!;
    private ButtonMenuItem _renameFolderMenuItem = null!;
    private readonly UITimer _layoutPollTimer;
    private readonly UITimer _invalidationTimer;
    private readonly UITimer _responsiveTimer;
    private readonly UITimer _thumbnailTimer;
    private readonly OverviewSelectionModel _selection = new();
    private readonly OverviewThumbnailCache _thumbnailCache = new();
    private readonly OverviewThumbnailRequestQueue _thumbnailQueue = new();
    private readonly Dictionary<OverviewThumbnailKey, Bitmap> _thumbnailBitmaps = [];
    private readonly Dictionary<Guid, HierarchyTreeItem> _sheetItems = [];
    private readonly HierarchyExpansionState _hierarchyExpansion = new();
    private IReadOnlyList<HierarchyTreeItem> _renderedTreeItems = [];
    private readonly object _invalidationSyncRoot = new();
    private DocumentOverview _overview = DocumentOverview.NoDocument;
    private OverviewFilterProjection _filterProjection = new(
        false,
        new HashSet<OverviewNodeKey>(),
        new HashSet<Guid>());
    private OverviewInvalidation? _pendingInvalidation;
    private CancellationTokenSource _thumbnailCancellation = new();
    private FoundryResponsiveLayout _responsiveLayout = FoundryResponsiveLayout.ForWidth(420);
    private uint? _documentSerialNumber;
    private bool _isLoaded;
    private bool _isPopulatingTree;
    private bool _isApplyingResponsiveLayout;
    private bool _thumbnailCaptureInProgress;
    private bool _dragInProgress;
    private OverviewSortProperty _sortProperty = OverviewSortProperty.None;
    private OverviewSortDirection _sortDirection = OverviewSortDirection.Ascending;
    private PointF? _dragStart;
    private HierarchyTreeItem? _dragSourceItem;
    private IReadOnlyList<OverviewNodeKey> _dragSourceKeys = [];
    private IReadOnlyList<OverviewNodeKey> _propertyInteractionTargets = [];
    private IReadOnlyList<OverviewNodeKey> _hierarchyDisplayModeTargets = [];
    private Dictionary<string, Guid> _hierarchyDisplayModes = new(StringComparer.OrdinalIgnoreCase);
    private OverviewNodeKey? _hierarchyDisplayModeEditingKey;
    private FilteredPicker? _activeHierarchyDisplayModePicker;
    private CellInteractionGuard? _cellInteractionGuard;
    private CellInteractionGuard? _selectionPreservingCircleInteraction;
    private InlineDraft? _inlineDraft;
    private Guid? _contextDestinationFolderId;
    private Guid? _contextPrintFolderId;
    private Guid? _folderDraftId;
    private Guid? _folderDraftParentId;
    private Form? _fullscreenWindow;
    private PendingDeleteSelection? _pendingDeleteSelection;
    private bool _deleteInProgress;
    private FoundryPanelViewMode _viewMode = FoundryPanelViewMode.List;

    public LayoutFoundryPanel()
    {
        BackgroundColor = FoundryTheme.PanelBackground;

        _emptyTitleLabel = new Label
        {
            Font = FoundryTheme.EmptyTitleFont,
            TextColor = FoundryTheme.PrimaryText,
            TextAlignment = TextAlignment.Center,
        };
        _emptyDescriptionLabel = FoundryTheme.MutedLabel();
        _emptyDescriptionLabel.TextAlignment = TextAlignment.Center;
        _summaryLabel = FoundryTheme.MutedLabel();
        _statusLabel = FoundryTheme.MutedLabel();
        _statusLabel.TextAlignment = TextAlignment.Left;

        _filterTextBox = new TextBox
        {
            PlaceholderText = "Search",
            ToolTip = "Search layouts, folders, and details",
            ShowBorder = false,
            BackgroundColor = Colors.Transparent,
        };
        _filterKindDropDown = new DropDown
        {
            ToolTip = "Choose which layout rows to show",
            ShowBorder = false,
            BackgroundColor = Colors.Transparent,
            DataStore = new[] { "All rows", "Sheets", "Details" },
            SelectedIndex = 0,
        };
        var searchInput = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space2,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new ImageView
                {
                    Image = FoundryViewIcons.Search(),
                    Size = new Size(18, 18),
                },
                new StackLayoutItem(_filterTextBox, expand: true),
            },
        };
        _searchField = new FoundryToolbarField(searchInput, 240, _filterTextBox);
        _filterKindField = new FoundryToolbarField(_filterKindDropDown, 96);
        _clearFilterButton = new FoundryToolbarIconButton(
            FoundryViewIcons.Close(),
            "Clear search and row filter")
        {
            Visible = false,
        };
        _renameTextBox = new TextBox
        {
            PlaceholderText = "Sheet name",
        };
        _renameField = new FoundryFormField(_renameTextBox);
        _renameButton = new FoundryDialogButton(
            "Rename",
            FoundryDialogButtonStyle.Secondary);
        _folderDraftDestinationLabel = FoundryTheme.MutedLabel();
        _folderDraftTextBox = new TextBox { PlaceholderText = "Folder name" };
        _folderDraftField = new FoundryFormField(_folderDraftTextBox);
        _folderDraftCreateButton = new FoundryDialogButton(
            "Create",
            FoundryDialogButtonStyle.Secondary);
        _folderDraftCancelButton = new FoundryDialogButton(
            "Cancel",
            FoundryDialogButtonStyle.Secondary);
        _folderDraftStrip = FoundryTheme.Surface(
            CreateFolderDraftContent(),
            new Padding(FoundryTheme.Space2));
        _folderDraftStrip.Visible = false;
        _createButton = new FoundryToolbarIconButton(
            FoundryViewIcons.Add(),
            "Create folder, layout, or appearance state")
        {
            Enabled = false,
        };
        _manageButton = new FoundryToolbarIconButton(
            FoundryViewIcons.Properties(),
            "Edit selected properties")
        {
            Enabled = false,
        };
        _deleteButton = new FoundryToolbarIconButton(
            FoundryViewIcons.Delete(),
            "Delete selected items")
        {
            Enabled = false,
        };
        _projectInfoButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ProjectInformation(),
            "Edit project information")
        {
            Enabled = false,
        };
        _importButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ImportPackage(),
            "Import layout package")
        {
            Enabled = false,
        };
        _exportButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ExportPackage(),
            "Export layout package")
        {
            Enabled = false,
        };
        _listViewButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ListView(),
            "List view",
            isToggle: true);
        _canvasViewButton = new FoundryToolbarIconButton(
            FoundryViewIcons.CartesianPlane(),
            "Canvas view (spatial board)",
            isToggle: true);
        _thumbnailViewButton = new FoundryToolbarIconButton(
            FoundryViewIcons.ThumbnailStack(),
            "Thumbnail view (page grid)",
            isToggle: true);
        _viewModeButtonGroup = new FoundryToolbarButtonGroup(
            _listViewButton,
            _thumbnailViewButton,
            _canvasViewButton);
        _fullscreenButton = new FoundryToolbarIconButton(
            FoundryViewIcons.Fullscreen(),
            "Expand the current view to a maximized workspace");
        _paperCell = new TextBoxCell
        {
            Binding = Binding.Property<HierarchyTreeItem, string>(item => item.PaperCellText),
        };
        _displayModeCell = CreateDisplayModeCell();
        (_treeGrid, _layoutsColumn, _printColumn, _templateColumn, _paperColumn, _detailsColumn,
            _displayModeColumn, _layersColumn, _objectModesColumn, _statusColumn) = CreateTreeGrid();
        CreateHierarchyContextMenu();
        _toolbarSurface = FoundryTheme.Surface(
            CreateToolbarContent(),
            new Padding(0));
        _contentHost = FoundryTheme.Surface(CreateEmptyState());
        _renameActions = CreateRenameActions();

        _managementView = new StackLayout
        {
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_contentHost, expand: true),
                _renameActions,
            },
        };
        _thumbnailView = new ThumbnailFoundryPanel();
        _thumbnailDensityControl = _thumbnailView.DensityControl;
        _thumbnailDensityControl.Visible = false;
        _observerView = new ObserverFoundryPanel();
        _observerView.ExitFullscreenRequested += (_, _) => ExitFullscreen();
        _thumbnailView.DeleteSelectionRequested += OnDeleteSelectionRequested;
        _observerView.DeleteSelectionRequested += OnDeleteSelectionRequested;
        _viewHost = new Panel { Content = _managementView };
        _panelShell = new StackLayout
        {
            BackgroundColor = FoundryTheme.PanelBackground,
            Padding = new Padding(FoundryTheme.Space4),
            Spacing = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayout
                {
                    Spacing = FoundryTheme.Space3,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        CreateHeader(),
                        _toolbarSurface,
                    },
                },
                _folderDraftStrip,
                new StackLayoutItem(_viewHost, expand: true),
                CreateBottomBar(),
            },
        };
        _deleteConfirmationOverlay = new DeleteConfirmationOverlay();
        _deleteConfirmationOverlay.CancelRequested += (_, _) => CancelDeleteConfirmation();
        _deleteConfirmationOverlay.ConfirmRequested += async (_, _) =>
            await ConfirmDeleteSelectionAsync();
        _templateRolesOverlay = new TemplateRolesOverlay();
        _templateRolesOverlay.CommitRequested += async (_, eventArgs) =>
            await CommitTemplateRolesAsync(eventArgs.Values);
        _createMenuOverlay = new CreateResourceMenuOverlay();
        _createMenuOverlay.ItemInvoked += (_, eventArgs) =>
            QueueCreateMenuItem(eventArgs.Kind);
        _panelOverlayHost = new PixelLayout
        {
            BackgroundColor = FoundryTheme.PanelBackground,
        };
        _panelOverlayHost.Add(_panelShell, 0, 0);
        _panelOverlayHost.Add(_createMenuOverlay, 0, 0);
        _panelOverlayHost.Add(_templateRolesOverlay, 0, 0);
        _panelOverlayHost.Add(_deleteConfirmationOverlay, 0, 0);
        _panelOverlayHost.SizeChanged += (_, _) => LayoutPanelOverlay();
        Content = _panelOverlayHost;
        UpdateViewModeButtons(FoundryPanelViewMode.List);

        _treeGrid.SelectedItemChanged += OnSelectionChanged;
        _treeGrid.CellFormatting += OnHierarchyCellFormatting;
        _treeGrid.CellDoubleClick += (_, eventArgs) =>
        {
            if ((eventArgs.Buttons & MouseButtons.Primary) == 0)
            {
                return;
            }

            if (eventArgs.Item is HierarchyTreeItem item &&
                ReferenceEquals(eventArgs.GridColumn, _layoutsColumn) &&
                IsInlineRenameTarget(item))
            {
                BeginInlineNameRename(item);
                return;
            }

            NavigateSelected();
        };
        _treeGrid.KeyDown += OnTreeKeyDown;
        _treeGrid.MouseDown += OnTreeMouseDown;
        _treeGrid.MouseMove += OnTreeMouseMove;
        _treeGrid.DragOver += OnTreeDragOver;
        _treeGrid.DragDrop += async (_, eventArgs) => await CompleteInternalDragAsync(eventArgs);
        _treeGrid.DragEnd += (_, _) => ResetPendingDrag();
        _treeGrid.CellClick += async (_, eventArgs) => await OnTreeCellClickAsync(eventArgs);
        _treeGrid.CellEdited += async (_, eventArgs) => await OnTreeCellEditedAsync(eventArgs);
        _treeGrid.ColumnHeaderClick += (_, eventArgs) => OnColumnHeaderClick(eventArgs);
        _treeGrid.Collapsed += (_, eventArgs) =>
        {
            if (!_isPopulatingTree && eventArgs.Item is HierarchyTreeItem item)
                _hierarchyExpansion.Record(item.Node.Key, expanded: false);
        };
        _treeGrid.Expanded += (_, eventArgs) =>
        {
            if (!_isPopulatingTree && eventArgs.Item is HierarchyTreeItem item)
                _hierarchyExpansion.Record(item.Node.Key, expanded: true);
        };
        _filterTextBox.TextChanged += (_, _) => OnFilterChanged();
        _filterTextBox.KeyDown += OnSearchKeyDown;
        _filterKindDropDown.SelectedIndexChanged += (_, _) => OnFilterChanged();
        _clearFilterButton.Click += (_, _) => ClearFilter();
        _createButton.Click += (_, _) => ToggleCreateMenu();
        _manageButton.Click += (_, _) => OpenSelectedProperties();
        _deleteButton.Click += (_, _) => RequestDeleteSelection(SelectedKeys());
        _projectInfoButton.Click += (_, _) => OpenProjectInformation();
        _importButton.Click += async (_, _) => await ImportLayoutPackageAsync();
        _exportButton.Click += async (_, _) => await ExportLayoutPackageAsync();
        _listViewButton.Click += (_, _) => ShowListView();
        _thumbnailViewButton.Click += (_, _) => ShowThumbnailView();
        _canvasViewButton.Click += (_, _) => ShowCanvasView();
        _fullscreenButton.Click += (_, _) => ToggleFullscreen();
        _renameButton.Click += async (_, _) => await RenameSelectedSheetAsync();
        _folderDraftCreateButton.Click += async (_, _) => await CommitFolderCreationAsync();
        _folderDraftCancelButton.Click += (_, _) => CancelFolderCreation();
        _folderDraftTextBox.KeyDown += async (_, eventArgs) =>
        {
            if (eventArgs.Key == Keys.Enter)
            {
                eventArgs.Handled = true;
                await CommitFolderCreationAsync();
            }
            else if (eventArgs.Key == Keys.Escape)
            {
                eventArgs.Handled = true;
                CancelFolderCreation();
            }
        };

        _layoutPollTimer = new UITimer { Interval = 0.5 };
        _layoutPollTimer.Elapsed += OnLayoutPoll;
        _invalidationTimer = new UITimer { Interval = 0.12 };
        _invalidationTimer.Elapsed += OnInvalidationTimer;
        _responsiveTimer = new UITimer { Interval = 0.16 };
        _responsiveTimer.Elapsed += (_, _) =>
        {
            _responsiveTimer.Stop();
            ApplyResponsiveLayout();
        };
        _thumbnailTimer = new UITimer { Interval = 0.08 };
        _thumbnailTimer.Elapsed += async (_, _) => await CaptureNextThumbnailAsync();
        Load += OnPanelLoaded;
        UnLoad += OnPanelUnloaded;
        // Rhino's macOS dock splitter runs a nested AppKit tracking loop. Any
        // managed Eto frame mutation scheduled from SizeChanged can recursively
        // enter NSView geometry validation. Mac density is chosen on panel load;
        // live breakpoint transitions remain enabled on Windows.
        if (!OperatingSystem.IsMacOS())
        {
            SizeChanged += (_, _) => QueueResponsiveLayout();
        }
        RefreshOverview();
    }

    private (TreeGridView TreeGrid, GridColumn LayoutsColumn, GridColumn PrintColumn,
        GridColumn TemplateColumn,
        GridColumn PaperColumn, GridColumn DetailsColumn, GridColumn DisplayModeColumn,
        GridColumn LayersColumn, GridColumn ObjectModesColumn, GridColumn StatusColumn) CreateTreeGrid()
    {
        var treeGrid = new TreeGridView
        {
            AllowMultipleSelection = true,
            AllowColumnReordering = false,
            AllowDrop = true,
            ShowHeader = true,
            RowHeight = 24,
        };
        var layoutsColumn = new GridColumn
        {
            HeaderText = "Layouts",
            DataCell = CreateLayoutsDataCell(),
            Width = 260,
            MinWidth = 220,
            Editable = false,
            Sortable = true,
        };
        treeGrid.Columns.Add(layoutsColumn);
        var printColumn = new GridColumn
        {
            HeaderText = "Print",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.PrintText),
                TextAlignment = TextAlignment.Center,
            },
            Width = 52,
            Sortable = true,
        };
        treeGrid.Columns.Add(printColumn);
        var templateColumn = new GridColumn
        {
            HeaderText = "Template roles",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.TemplateText),
            },
            Width = 156,
            Sortable = true,
        };
        treeGrid.Columns.Add(templateColumn);
        var paperColumn = new GridColumn
        {
            HeaderText = "Paper size",
            DataCell = _paperCell,
            Width = 178,
            Editable = false,
            Sortable = true,
        };
        treeGrid.Columns.Add(paperColumn);
        var detailsColumn = new GridColumn
        {
            HeaderText = "Details",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.DetailsText),
            },
            Width = 70,
            Sortable = true,
        };
        treeGrid.Columns.Add(detailsColumn);
        var displayModeColumn = new GridColumn
        {
            HeaderText = "Display mode",
            DataCell = _displayModeCell,
            Width = 175,
            Editable = false,
            Sortable = true,
        };
        treeGrid.Columns.Add(displayModeColumn);
        var layersColumn = new GridColumn
        {
            HeaderText = "Layers",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.LayerStateText),
            },
            Width = 142,
            Sortable = false,
        };
        treeGrid.Columns.Add(layersColumn);
        var objectModesColumn = new GridColumn
        {
            HeaderText = "Object modes",
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<HierarchyTreeItem, string>(item => item.ObjectModesText),
            },
            Width = 144,
            Sortable = false,
        };
        treeGrid.Columns.Add(objectModesColumn);
        var statusColumn = new GridColumn
        {
            HeaderText = "Status",
            DataCell = new FoundryBadgeCell<HierarchyTreeItem>(
                item => item.StatusText,
                item => item.StatusTone),
            Width = 96,
            Sortable = true,
        };
        treeGrid.Columns.Add(statusColumn);

        return (treeGrid, layoutsColumn, printColumn, templateColumn, paperColumn, detailsColumn,
            displayModeColumn, layersColumn, objectModesColumn, statusColumn);
    }

    private void OnHierarchyCellFormatting(
        object? sender,
        GridCellFormatEventArgs eventArgs)
    {
        if (eventArgs.Item is not HierarchyTreeItem item ||
            item.Node.Key.Kind != OverviewNodeKind.Folder)
        {
            return;
        }

        if (_treeGrid.SelectedItems
            .OfType<HierarchyTreeItem>()
            .Any(selected => selected.Node.Key == item.Node.Key))
        {
            eventArgs.BackgroundColor = SystemColors.Selection;
            eventArgs.ForegroundColor = SystemColors.SelectionText;
            return;
        }

        eventArgs.BackgroundColor = item.Node.IsDocumentRoot
            ? FoundryTheme.HierarchyDocumentBackground
            : FoundryTheme.HierarchyFolderBackground;
    }

    private static Cell CreateLayoutsDataCell()
    {
        return new ImageTextCell(
            nameof(HierarchyTreeItem.RowIcon),
            nameof(HierarchyTreeItem.DisplayText))
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private CustomCell CreateDisplayModeCell()
    {
        var cell = new CustomCell
        {
            GetIdentifier = eventArgs => eventArgs.Item is HierarchyTreeItem item &&
                                         _hierarchyDisplayModeEditingKey == item.Node.Key
                ? "foundry-display-mode-editor"
                : "foundry-display-mode-label",
            CreateCell = eventArgs =>
            {
                if (eventArgs.Item is HierarchyTreeItem item &&
                    _hierarchyDisplayModeEditingKey == item.Node.Key)
                {
                    FilteredPicker? picker = null;
                    picker = new FilteredPicker(
                        [],
                        "Search display modes",
                        controlHeight: 24);
                    picker.SelectionCommitted += async (_, _) =>
                    {
                        if (ReferenceEquals(_activeHierarchyDisplayModePicker, picker))
                            await CommitHierarchyDisplayModeAsync();
                    };
                    picker.DismissRequested += (_, _) =>
                    {
                        if (ReferenceEquals(_activeHierarchyDisplayModePicker, picker))
                            CloseHierarchyDisplayModePicker();
                    };
                    return picker;
                }

                return new Label
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Left,
                };
            },
            ConfigureCell = (eventArgs, control) =>
            {
                if (eventArgs.Item is not HierarchyTreeItem item)
                    return;

                if (control is Label label)
                {
                    label.Text = item.DisplayModeCellText;
                    label.TextColor = eventArgs.CellTextColor;
                    return;
                }

                if (control is not FilteredPicker picker ||
                    _hierarchyDisplayModeEditingKey != item.Node.Key)
                {
                    return;
                }

                picker.SetChoices(_hierarchyDisplayModes.Keys);
                picker.Text = _hierarchyDisplayModes.ContainsKey(item.DisplayModeText)
                    ? item.DisplayModeText
                    : string.Empty;
                picker.Enabled = item.HasDetailTargets;
                _activeHierarchyDisplayModePicker = picker;
            },
        };

        return cell;
    }

    private void SetInlineEditing(bool enabled)
    {
        _layoutsColumn.Editable = enabled;
    }

    private Control CreateHeader()
    {
        var title = new Label
        {
            Text = "Layout Foundry",
            Font = SystemFonts.Bold(14),
            TextColor = FoundryTheme.PrimaryText,
            TextAlignment = TextAlignment.Left,
        };

        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space2,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                new ImageView { Image = FoundryViewIcons.BrandMark() },
                title,
                new StackLayoutItem(null, true),
            },
        };
    }

    private void OpenProjectInformation()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
        {
            _statusLabel.Text = "Open a Rhino document before editing project information.";
            return;
        }
        new ProjectInformationDialog(snapshot.ProjectInfo).ShowModal(this);
        RefreshOverview();
    }

    public void ShowListView() => SetViewMode(FoundryPanelViewMode.List);

    public void ShowThumbnailView() => SetViewMode(FoundryPanelViewMode.Thumbnail);

    public void ShowCanvasView() => SetViewMode(FoundryPanelViewMode.Canvas);

    private async Task ExportLayoutPackageAsync()
    {
        var context = LayoutFoundryUiHost.CaptureDocumentContext();
        if (context is null)
        {
            _statusLabel.Text = "Open a Rhino document before exporting a layout package.";
            return;
        }
        var safeName = string.Concat(_overview.DocumentName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        if (safeName.Length == 0) safeName = "Layouts";
        var dialog = new SaveFileDialog
        {
            Title = "Export Layout Package",
            FileName = $"{safeName}.rlf",
        };
        dialog.Filters.Add(new FileFilter("Layout Foundry package", ".rlf"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        var filePath = dialog.FileName.EndsWith(".rlf", StringComparison.OrdinalIgnoreCase)
            ? dialog.FileName
            : $"{dialog.FileName}.rlf";
        _statusLabel.Text = "Exporting layout package…";
        var result = await LayoutFoundryUiHost.ExportLayoutPackageAsync(new LayoutPackageExportRequest(
            context.Value.DocumentRuntimeSerialNumber,
            context.Value.Revision,
            filePath));
        _statusLabel.Text = result.Succeeded
            ? $"Exported {result.LayoutCount} layout{(result.LayoutCount == 1 ? string.Empty : "s")} to {Path.GetFileName(filePath)}."
            : result.ErrorMessage ?? "Layout package export failed.";
    }

    private async Task ImportLayoutPackageAsync()
    {
        var context = LayoutFoundryUiHost.CaptureDocumentContext();
        if (context is null)
        {
            _statusLabel.Text = "Open a Rhino document before importing a layout package.";
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Import Layout Package",
            MultiSelect = false,
        };
        dialog.Filters.Add(new FileFilter("Layout Foundry package", ".rlf"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        _statusLabel.Text = "Validating layout package…";
        var preflight = await LayoutFoundryUiHost.PreflightLayoutPackageAsync(dialog.FileName);
        if (!preflight.IsValid || preflight.Manifest is null)
        {
            _statusLabel.Text = preflight.ErrorMessage ?? "The layout package is invalid.";
            MessageBox.Show(this, _statusLabel.Text, "Import Layout Package", MessageBoxType.Error);
            return;
        }
        var review = new LayoutPackageImportDialog(preflight);
        review.ShowModal(this);
        if (!review.Accepted) return;

        context = LayoutFoundryUiHost.CaptureDocumentContext();
        if (context is null)
        {
            _statusLabel.Text = "The active Rhino document changed before import.";
            return;
        }
        _statusLabel.Text = $"Importing {preflight.Manifest.Sheets.Count} layout(s)…";
        var result = await LayoutFoundryUiHost.ImportLayoutPackageAsync(new LayoutPackageImportRequest(
            context.Value.DocumentRuntimeSerialNumber,
            context.Value.Revision,
            dialog.FileName,
            review.ImportMode,
            review.ConflictResolutions,
            review.ImportProjectInformation));
        _statusLabel.Text = result.Succeeded
            ? $"Imported {result.LayoutCount} layout{(result.LayoutCount == 1 ? string.Empty : "s")}."
            : result.ErrorMessage ?? "Layout package import failed.";
        if (!result.Succeeded && result.RecoveryPackagePath is not null)
            _statusLabel.Text += $" Recovery package: {result.RecoveryPackagePath}";
        if (result.Succeeded) RefreshOverview();
    }

    private void SetViewMode(FoundryPanelViewMode mode)
    {
        _viewMode = mode;
        var next = mode switch
        {
            FoundryPanelViewMode.Thumbnail => _thumbnailView,
            FoundryPanelViewMode.Canvas => _observerView,
            _ => _managementView,
        };
        if (!ReferenceEquals(_viewHost.Content, next))
            _viewHost.Content = next;
        if (mode == FoundryPanelViewMode.Thumbnail)
            _thumbnailView.PrepareForDisplay();
        UpdateViewModeButtons(mode);
        if (_fullscreenWindow is not null)
            _fullscreenWindow.Title = $"Layout Foundry — {ViewModeLabel(mode)}";
    }

    private void UpdateViewModeButtons(FoundryPanelViewMode mode)
    {
        _listViewButton.Checked = mode == FoundryPanelViewMode.List;
        _thumbnailViewButton.Checked = mode == FoundryPanelViewMode.Thumbnail;
        _canvasViewButton.Checked = mode == FoundryPanelViewMode.Canvas;
        _thumbnailDensityControl.Visible = mode == FoundryPanelViewMode.Thumbnail;
        _fullscreenButton.Checked = _fullscreenWindow is not null;
        _fullscreenButton.ToolTip = _fullscreenWindow is null
            ? $"Expand {ViewModeLabel(mode)} to a maximized workspace"
            : "Return Layout Foundry to the Rhino panel (Esc)";
        _createButton.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
    }

    private void ToggleCreateMenu()
    {
        if (_createMenuOverlay.Visible)
        {
            _createMenuOverlay.Dismiss();
            return;
        }
        if (_overview.DocumentRuntimeSerialNumber is null) return;
        var screenAnchor = _createButton.PointToScreen(new PointF(0, _createButton.Height));
        var anchor = _panelOverlayHost.PointFromScreen(screenAnchor);
        _createMenuOverlay.ShowMenu(new Point(
            (int)Math.Round(anchor.X),
            (int)Math.Round(anchor.Y + FoundryTheme.Space1)));
    }

    private void QueueCreateMenuItem(CreateResourceKind kind)
    {
        Application.Instance.AsyncInvoke(() =>
        {
            _ = InvokeCreateMenuItemSafelyAsync(kind);
        });
    }

    private async Task InvokeCreateMenuItemSafelyAsync(CreateResourceKind kind)
    {
        try
        {
            await InvokeCreateMenuItemAsync(kind);
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"Could not start creation: {exception.Message}";
        }
    }

    private Task InvokeCreateMenuItemAsync(CreateResourceKind kind)
    {
        var destination = ResolveCreationDestinationFolderId();
        switch (kind)
        {
            case CreateResourceKind.Folder:
                BeginFolderCreation(destination);
                break;
            case CreateResourceKind.Layout:
                OpenCreateLayouts(destination);
                break;
            case CreateResourceKind.LayerState:
                PromptAppearanceStateCreation(AppearanceStateKind.LayerState, destination);
                break;
            case CreateResourceKind.ObjectDisplayState:
                PromptAppearanceStateCreation(AppearanceStateKind.ObjectDisplayState, destination);
                break;
        }

        return Task.CompletedTask;
    }

    private void QueueAppearanceStateCreation(AppearanceStateKind kind, Guid? destinationFolderId)
    {
        Application.Instance.AsyncInvoke(() =>
        {
            try
            {
                PromptAppearanceStateCreation(kind, destinationFolderId);
            }
            catch (Exception exception)
            {
                _statusLabel.Text = $"Could not start creation: {exception.Message}";
            }
        });
    }

    private void PromptAppearanceStateCreation(AppearanceStateKind kind, Guid? destinationFolderId = null)
    {
        if (_overview.DocumentRuntimeSerialNumber is null) return;
        var resolvedFolderId = destinationFolderId ?? ResolveCreationDestinationFolderId() ?? _overview.RootFolderId;
        if (resolvedFolderId is not { } folderId)
        {
            _statusLabel.Text = "The layout root is unavailable.";
            return;
        }
        var folderName = FolderDestinationName(folderId);
        var siblingNames = _overview.AppearanceStates
            .Where(state => state.FolderId == folderId)
            .Select(state => state.Name);
        var dialog = new AppearanceStateNameDialog(kind, folderName, siblingNames);
        dialog.ShowModal(this);
        if (!dialog.Accepted) return;

        var name = dialog.StateName;
        Application.Instance.AsyncInvoke(async () =>
        {
            try
            {
                await CommitAppearanceStateCreationAsync(folderId, name, kind);
            }
            catch (Exception exception)
            {
                _statusLabel.Text = $"Could not create the appearance state: {exception.Message}";
            }
        });
    }

    private async Task CommitAppearanceStateCreationAsync(
        Guid folderId,
        string name,
        AppearanceStateKind kind)
    {
        _statusLabel.Text = kind == AppearanceStateKind.LayerState
            ? "Creating layer state…"
            : "Creating object display state…";
        var result = await LayoutFoundryUiHost.CreateAppearanceStateAsync(folderId, name, kind);
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        _overview = LayoutFoundryUiHost.CaptureOverview();
        var created = _overview.AppearanceStates.LastOrDefault(state =>
            state.FolderId == folderId && state.Kind == kind &&
            string.Equals(state.Name, name, StringComparison.OrdinalIgnoreCase));
        if (created is not null)
        {
            var key = new OverviewNodeKey(
                kind == AppearanceStateKind.LayerState
                    ? OverviewNodeKind.LayerState
                    : OverviewNodeKind.ObjectDisplayState,
                created.Id);
            _selection.Replace([key], key);
            LayoutFoundryUiHost.Selection.Replace(
                _overview.DocumentRuntimeSerialNumber,
                [key],
                key,
                this);
        }

        _statusLabel.Text = kind == AppearanceStateKind.LayerState
            ? $"Created layer state '{name}'."
            : $"Created object display state '{name}'.";
        PopulateTree();
    }

    private void ToggleFullscreen()
    {
        if (_fullscreenWindow is not null)
        {
            _fullscreenWindow.Close();
            return;
        }

        Content = null;
        var window = new Form
        {
            Title = $"Layout Foundry — {ViewModeLabel(_viewMode)}",
            BackgroundColor = FoundryTheme.PanelBackground,
            MinimumSize = new Size(720, 480),
            WindowState = WindowState.Maximized,
            Content = _panelOverlayHost,
        };
        _fullscreenWindow = window;
        _fullscreenButton.Image = FoundryViewIcons.ExitFullscreen();
        _observerView.SetFullscreenState(true);
        UpdateViewModeButtons(_viewMode);
        window.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Keys.Escape)
                return;

            eventArgs.Handled = true;
            if (_pendingDeleteSelection is not null)
            {
                CancelDeleteConfirmation();
                return;
            }

            window.Close();
        };
        window.Closing += (_, _) => RestoreFromFullscreen(window);
        window.Show();
    }

    private void ExitFullscreen()
    {
        _fullscreenWindow?.Close();
    }

    private void RestoreFromFullscreen(Form window)
    {
        if (!ReferenceEquals(_fullscreenWindow, window))
            return;

        window.Content = null;
        _fullscreenWindow = null;
        _fullscreenButton.Image = FoundryViewIcons.Fullscreen();
        _observerView.SetFullscreenState(false);
        Content = _panelOverlayHost;
        UpdateViewModeButtons(_viewMode);
    }

    private void LayoutPanelOverlay()
    {
        CloseHierarchyDisplayModePicker();
        var size = _panelOverlayHost.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        _panelShell.Size = size;
        _createMenuOverlay.Size = size;
        _templateRolesOverlay.Size = size;
        _deleteConfirmationOverlay.Size = size;
        _panelOverlayHost.Move(_panelShell, 0, 0);
        _panelOverlayHost.Move(_createMenuOverlay, 0, 0);
        _panelOverlayHost.Move(_templateRolesOverlay, 0, 0);
        _panelOverlayHost.Move(_deleteConfirmationOverlay, 0, 0);
    }

    private static string ViewModeLabel(FoundryPanelViewMode mode) => mode switch
    {
        FoundryPanelViewMode.Thumbnail => "Thumbnails",
        FoundryPanelViewMode.Canvas => "Canvas",
        _ => "Hierarchy",
    };

    private Control CreateToolbarContent()
    {
        _searchField.Width = _responsiveLayout.StackToolbar ? 240 : 320;
        _filterKindField.Width = _responsiveLayout.StackToolbar ? 84 : 96;
        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space1,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _createButton,
                _manageButton,
                _deleteButton,
                new Panel
                {
                    Width = 1,
                    Height = 20,
                    BackgroundColor = FoundryTheme.CanvasBorder,
                },
                _projectInfoButton,
                new Panel
                {
                    Width = 1,
                    Height = 20,
                    BackgroundColor = FoundryTheme.CanvasBorder,
                },
                _importButton,
                _exportButton,
                new Panel
                {
                    Width = 1,
                    Height = 20,
                    BackgroundColor = FoundryTheme.CanvasBorder,
                },
                _searchField,
                _filterKindField,
                _clearFilterButton,
                new StackLayoutItem(null, expand: true),
            },
        };
    }

    private Control CreateBottomBar() => new StackLayout
    {
        Orientation = Orientation.Horizontal,
        Padding = new Padding(0, FoundryTheme.Space1, 0, 0),
        Spacing = FoundryTheme.Space1,
        VerticalContentAlignment = VerticalAlignment.Center,
        Items =
        {
            _fullscreenButton,
            new Panel
            {
                Width = 1,
                Height = 20,
                BackgroundColor = FoundryTheme.CanvasBorder,
            },
            _viewModeButtonGroup,
            _thumbnailDensityControl,
            new StackLayoutItem(_statusLabel, expand: true),
            _summaryLabel,
        },
    };

    private void CreateHierarchyContextMenu()
    {
        _setCurrentMenuItem = new ButtonMenuItem { Text = "Set Current" };
        _newFolderMenuItem = new ButtonMenuItem { Text = "New Folder" };
        _newPageMenuItem = new ButtonMenuItem { Text = "New Layout…" };
        _newLayerStateMenuItem = new ButtonMenuItem { Text = "New Layer State" };
        _newObjectStateMenuItem = new ButtonMenuItem { Text = "New Object Display State" };
        _duplicateSelectionMenuItem = new ButtonMenuItem { Text = "Duplicate" };
        _copySelectionMenuItem = new ButtonMenuItem { Text = "Copy" };
        _pasteSelectionMenuItem = new ButtonMenuItem { Text = "Paste" };
        _deleteSelectionMenuItem = new ButtonMenuItem { Text = "Delete…" };
        _renamePageMenuItem = new ButtonMenuItem { Text = "Rename" };
        _newDetailMenuItem = new ButtonMenuItem { Text = "New Detail" };
        _printPageMenuItem = new ButtonMenuItem { Text = "Print…" };
        _printScopeMenuItem = new ButtonMenuItem { Text = "Print Enabled…" };
        _propertiesPageMenuItem = new ButtonMenuItem { Text = "Layout Properties…" };
        _renameFolderMenuItem = new ButtonMenuItem { Text = "Rename Folder…" };

        _setCurrentMenuItem.Click += (_, _) => NavigateSelected();
        _newFolderMenuItem.Click += (_, _) =>
            BeginInlineCreation(InlineDraftKind.Folder, _contextDestinationFolderId);
        _newPageMenuItem.Click += (_, _) => QueueOpenCreateLayouts(_contextDestinationFolderId);
        _newLayerStateMenuItem.Click += (_, _) =>
            QueueAppearanceStateCreation(AppearanceStateKind.LayerState, _contextDestinationFolderId);
        _newObjectStateMenuItem.Click += (_, _) =>
            QueueAppearanceStateCreation(AppearanceStateKind.ObjectDisplayState, _contextDestinationFolderId);
        _duplicateSelectionMenuItem.Click += async (_, _) => await DuplicateSelectionAsync();
        _copySelectionMenuItem.Click += (_, _) => CopySelection();
        _pasteSelectionMenuItem.Click += async (_, _) => await PasteSelectionAsync();
        _deleteSelectionMenuItem.Click += (_, _) => RequestDeleteSelection(SelectedKeys());
        _renamePageMenuItem.Click += (_, _) => BeginInlineSheetRename();
        _newDetailMenuItem.Click += (_, _) => RunSelectedSheetCommand(LayoutSheetCommand.NewDetail);
        _printPageMenuItem.Click += (_, _) => RunSelectedSheetCommand(LayoutSheetCommand.Print);
        _printScopeMenuItem.Click += async (_, _) => await PrintHierarchyScopeAsync();
        _propertiesPageMenuItem.Click += (_, _) => OpenSelectedProperties();
        _renameFolderMenuItem.Click += async (_, _) => await RenameSelectedFolderAsync();

        var contextMenu = new ContextMenu(
            _setCurrentMenuItem,
            new SeparatorMenuItem(),
            _newFolderMenuItem,
            _newPageMenuItem,
            _newLayerStateMenuItem,
            _newObjectStateMenuItem,
            new SeparatorMenuItem(),
            _copySelectionMenuItem,
            _pasteSelectionMenuItem,
            new SeparatorMenuItem(),
            _duplicateSelectionMenuItem,
            _deleteSelectionMenuItem,
            _renamePageMenuItem,
            new SeparatorMenuItem(),
            _newDetailMenuItem,
            new SeparatorMenuItem(),
            _printPageMenuItem,
            _printScopeMenuItem,
            _propertiesPageMenuItem,
            new SeparatorMenuItem(),
            _renameFolderMenuItem);
        contextMenu.Opening += (_, _) => UpdateContextMenuActions();
        _treeGrid.ContextMenu = contextMenu;
    }

    private Control CreateEmptyState()
    {
        return new StackLayout
        {
            Padding = new Padding(FoundryTheme.Space6),
            Spacing = FoundryTheme.Space2,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Items =
            {
                _emptyTitleLabel,
                _emptyDescriptionLabel,
            },
        };
    }

    private Panel CreateRenameActions()
    {
        return new Panel
        {
            Content = new StackLayout
            {
                Orientation = Orientation.Horizontal,
                Spacing = FoundryTheme.Space2,
                VerticalContentAlignment = VerticalAlignment.Center,
                Items =
                {
                    new StackLayoutItem(_renameField, expand: true),
                    _renameButton,
                },
            },
        };
    }

    private Control CreateFolderDraftContent()
    {
        if (_responsiveLayout.StackToolbar)
        {
            return new StackLayout
            {
                Spacing = FoundryTheme.Space1,
                Items =
                {
                    _folderDraftDestinationLabel,
                    _folderDraftField,
                    new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = FoundryTheme.Space1,
                        Items =
                        {
                            new StackLayoutItem(null, expand: true),
                            _folderDraftCancelButton,
                            _folderDraftCreateButton,
                        },
                    },
                },
            };
        }

        return new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space2,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _folderDraftDestinationLabel,
                new StackLayoutItem(_folderDraftField, expand: true),
                _folderDraftCancelButton,
                _folderDraftCreateButton,
            },
        };
    }

    private OverviewTreeFilter CurrentFilter => new(
        _filterTextBox.Text,
        _filterKindDropDown.SelectedIndex switch
        {
            1 => OverviewFilterKind.Sheets,
            2 => OverviewFilterKind.Details,
            _ => OverviewFilterKind.All,
        });

    private void OnFilterChanged()
    {
        _clearFilterButton.Visible =
            !string.IsNullOrWhiteSpace(_filterTextBox.Text) ||
            _filterKindDropDown.SelectedIndex != 0;
        PopulateTree();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Keys.Escape)
            return;

        eventArgs.Handled = true;
        Application.Instance.AsyncInvoke(FocusActiveView);
    }

    private void FocusActiveView()
    {
        switch (_viewMode)
        {
            case FoundryPanelViewMode.Thumbnail:
                _thumbnailView.FocusContent();
                break;
            case FoundryPanelViewMode.Canvas:
                _observerView.FocusContent();
                break;
            default:
                _treeGrid.Focus();
                break;
        }
    }

    private void ClearFilter()
    {
        _filterTextBox.Text = string.Empty;
        _filterKindDropDown.SelectedIndex = 0;
    }

    private void OnSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_isPopulatingTree)
        {
            return;
        }

        var selectedItems = SelectedItems();
        var anchor = (_treeGrid.SelectedItem as HierarchyTreeItem)?.Node.Key;
        _selection.Replace(selectedItems.Select(item => item.Node.Key), anchor);
        LayoutFoundryUiHost.Selection.Replace(
            _overview.DocumentRuntimeSerialNumber,
            _selection.Selected,
            _selection.Anchor,
            this);
        UpdatePresentation();
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_inlineDraft is null && HierarchyClipboard.IsCopyShortcut(eventArgs))
        {
            CopySelection();
            eventArgs.Handled = true;
        }
        else if (_inlineDraft is null && HierarchyClipboard.IsPasteShortcut(eventArgs))
        {
            _ = PasteSelectionAsync();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Enter)
        {
            if (_inlineDraft is not null)
            {
                _treeGrid.CommitEdit();
                eventArgs.Handled = true;
                return;
            }

            NavigateSelected();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Escape)
        {
            if (_inlineDraft is { } draft)
            {
                _inlineDraft = null;
                SetInlineEditing(false);
                _treeGrid.CancelEdit();
                if (IsRenameDraft(draft.Kind))
                {
                    var item = Flatten(_renderedTreeItems)
                        .FirstOrDefault(candidate => candidate.Node.Key.Id == draft.Id);
                    if (item is not null)
                    {
                        item.CancelInlineEdit();
                        _treeGrid.ReloadItem(item, reloadChildren: false);
                    }

                    _statusLabel.Text = "Rename cancelled.";
                }
                else
                {
                    PopulateTree();
                    _statusLabel.Text = "Creation cancelled.";
                }

                eventArgs.Handled = true;
                return;
            }

            ClearSelection();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Modifiers == Keys.None &&
                 eventArgs.Key is Keys.Delete or Keys.Backspace &&
                 SelectedItemCount() > 0 &&
                 SelectedItems().All(item => !item.Node.IsDocumentRoot))
        {
            RequestDeleteSelection(SelectedKeys());
            eventArgs.Handled = true;
        }
    }

    private void NavigateSelected()
    {
        var targets = SelectedItems()
            .Select(item => item.Node.NavigationTarget)
            .Where(target => target is not null)
            .Select(target => target!.Value)
            .Take(2)
            .ToArray();
        if (targets.Length != 1 || SelectedItemCount() != 1)
        {
            return;
        }

        var result = LayoutFoundryUiHost.Navigate(targets[0]);
        _statusLabel.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private async Task PrintHierarchyScopeAsync()
    {
        var scope = LayoutPrintScopeResolver.Resolve(_overview, _contextPrintFolderId);
        if (!scope.Exists)
        {
            _statusLabel.Text = "That folder no longer exists. Refresh and try again.";
            return;
        }

        if (!scope.HasSheets || _overview.DocumentRuntimeSerialNumber is not { } documentSerialNumber)
        {
            _statusLabel.Text = scope.HasSheets
                ? "The active Rhino document is unavailable."
                : "There are no layouts in this print scope.";
            return;
        }

        var safeName = string.Concat(scope.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character)).Trim();
        if (safeName.Length == 0)
        {
            safeName = "Layouts";
        }

        var dialog = new SaveFileDialog
        {
            Title = _contextPrintFolderId is null ? "Print Enabled Layouts" : $"Print {scope.Name}",
            FileName = $"{safeName}.pdf",
        };
        dialog.Filters.Add(new FileFilter("PDF document", ".pdf"));
        if (dialog.ShowDialog(this) != DialogResult.Ok)
        {
            return;
        }

        var filePath = dialog.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            ? dialog.FileName
            : $"{dialog.FileName}.pdf";
        _statusLabel.Text = $"Creating {scope.SheetPageViewIds.Count}-page PDF…";
        await Task.Yield();
        var result = await LayoutFoundryUiHost.ExportPdfAsync(new LayoutPdfExportRequest(
            documentSerialNumber,
            scope.SheetPageViewIds,
            filePath));
        _statusLabel.Text = result.Succeeded
            ? $"Printed {result.PageCount} layout{(result.PageCount == 1 ? string.Empty : "s")} to {Path.GetFileName(filePath)}."
            : result.Message;
    }

    private async Task RenameSelectedSheetAsync()
    {
        var selected = SelectedSheets().Take(2).ToArray();
        if (selected.Length != 1 || SelectedItemCount() != 1)
        {
            return;
        }

        _renameButton.Enabled = false;
        _renameTextBox.Enabled = false;
        _statusLabel.Text = "Applying rename…";

        var result = await LayoutFoundryUiHost.RenameSheetAsync(
            selected[0].PageViewId,
            selected[0].Name,
            _renameTextBox.Text);

        _statusLabel.Text = result.Succeeded
            ? "Layout renamed."
            : string.Join(" ", result.Diagnostics.Select(item => item.Message));

        if (!result.Succeeded)
        {
            _renameButton.Enabled = true;
            _renameTextBox.Enabled = true;
        }
    }

    private async Task DuplicateSelectionAsync()
    {
        var keys = SelectedKeys();
        if (keys.Any(IsDocumentRootKey))
        {
            _statusLabel.Text = "The project root cannot be duplicated.";
            return;
        }
        if (keys.Length == 0) return;
        _statusLabel.Text = $"Duplicating {keys.Length} selected item{(keys.Length == 1 ? string.Empty : "s")}…";
        var result = await LayoutFoundryUiHost.DuplicateSelectionAsync(keys);
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        _statusLabel.Text = $"Duplicated {keys.Length} selected item{(keys.Length == 1 ? string.Empty : "s")}.";
        RefreshOverview();
    }

    private void CopySelection()
    {
        _statusLabel.Text = HierarchyClipboard.CopyCurrentSelection().Message;
    }

    private async Task PasteSelectionAsync()
    {
        var result = await HierarchyClipboard.PasteAsync();
        _statusLabel.Text = result.Message;
        if (result.Succeeded) RefreshOverview();
    }

    private void OnDeleteSelectionRequested(
        object? sender,
        DeleteSelectionRequestedEventArgs eventArgs) =>
        RequestDeleteSelection(eventArgs.Selection);

    private void RequestDeleteSelection(IReadOnlyList<OverviewNodeKey> requestedSelection)
    {
        if (_pendingDeleteSelection is not null || _deleteInProgress)
            return;

        var keys = requestedSelection.Distinct().ToArray();
        if (keys.Any(IsDocumentRootKey))
        {
            _statusLabel.Text = "The project root cannot be deleted.";
            return;
        }

        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (keys.Length == 0 || snapshot is null)
            return;

        var resolved = HierarchySelectionResolver.Resolve(snapshot, keys);
        if (resolved.SelectedItemCount == 0 || resolved.UnresolvedKeys.Count > 0)
        {
            _statusLabel.Text = "One or more selected items no longer exist. Refresh and try again.";
            return;
        }

        var folderCount = resolved.ExpandedFolderIds.Count;
        var sheetCount = resolved.AllSheetPageViewIds.Count;
        var stateIds = resolved.StandaloneAppearanceStateIds
            .Concat(snapshot.AppearanceStates
                .Where(state => resolved.ExpandedFolderIds.Contains(state.FolderId))
                .Select(state => state.Id))
            .Distinct().ToHashSet();
        var stateCount = stateIds.Count;
        var parts = new List<string>();
        if (folderCount > 0) parts.Add($"{folderCount} folder{(folderCount == 1 ? string.Empty : "s")}");
        if (sheetCount > 0) parts.Add($"{sheetCount} Rhino layout{(sheetCount == 1 ? string.Empty : "s")}");
        if (stateCount > 0) parts.Add($"{stateCount} appearance state{(stateCount == 1 ? string.Empty : "s")}");
        var summary = parts.Count switch
        {
            0 => "selected items",
            1 => parts[0],
            2 => string.Join(" and ", parts),
            _ => $"{string.Join(", ", parts.Take(parts.Count - 1))}, and {parts[^1]}",
        };
        var pending = new PendingDeleteSelection(
            snapshot.DocumentRuntimeSerialNumber,
            snapshot.Revision,
            keys,
            summary);

        if (sheetCount == 0 && stateCount == 0)
        {
            _ = ExecuteDeleteSelectionAsync(pending, showBusyOverlay: false);
            return;
        }

        _pendingDeleteSelection = pending;
        _panelShell.Enabled = false;
        _deleteConfirmationOverlay.ShowConfirmation(
            summary,
            resolved.SelectedItemCount == 1,
            stateCount == 0
                ? null
                : AppearanceStateDeletionMessage(stateIds));
    }

    private string AppearanceStateDeletionMessage(IReadOnlySet<Guid> stateIds)
    {
        var states = _overview.AppearanceStates.Where(state => stateIds.Contains(state.Id)).ToArray();
        var folders = states.Sum(state => state.DependentFolderCount);
        var sheets = states.Sum(state => state.DependentSheetCount);
        var details = states.Sum(state => state.DependentDetailCount);
        return $"The selected states currently affect {folders} folder{(folders == 1 ? string.Empty : "s")}, " +
               $"{sheets} layout{(sheets == 1 ? string.Empty : "s")}, and " +
               $"{details} detail{(details == 1 ? string.Empty : "s")}. Their assignments will return to inherit; " +
               "all explicitly authored local overrides will remain. This action cannot be undone.";
    }

    private async Task ConfirmDeleteSelectionAsync()
    {
        if (_pendingDeleteSelection is not { } pending || _deleteInProgress)
            return;

        var current = LayoutFoundryUiHost.CaptureSnapshot();
        if (current is null ||
            current.DocumentRuntimeSerialNumber != pending.DocumentRuntimeSerialNumber ||
            current.Revision != pending.SourceRevision)
        {
            _pendingDeleteSelection = null;
            DismissDeleteOverlay();
            _statusLabel.Text = "The Rhino document changed. Review the selection and try deleting again.";
            RefreshOverview();
            return;
        }

        await ExecuteDeleteSelectionAsync(pending, showBusyOverlay: true);
    }

    private async Task ExecuteDeleteSelectionAsync(
        PendingDeleteSelection pending,
        bool showBusyOverlay)
    {
        if (_deleteInProgress)
            return;

        _deleteInProgress = true;
        if (showBusyOverlay)
            _deleteConfirmationOverlay.ShowBusy(pending.Summary);
        _statusLabel.Text = $"Deleting {pending.Summary}…";

        OperationResult result;
        try
        {
            result = await LayoutFoundryUiHost.DeleteSelectionAsync(pending.Selection);
        }
        catch (Exception exception)
        {
            _statusLabel.Text = $"Deletion failed: {exception.Message}";
            RefreshOverview();
            LayoutFoundryUiHost.NotifyOverviewChanged(OverviewInvalidation.All);
            return;
        }
        finally
        {
            _deleteInProgress = false;
            _pendingDeleteSelection = null;
            DismissDeleteOverlay();
        }

        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            RefreshOverview();
            LayoutFoundryUiHost.NotifyOverviewChanged(OverviewInvalidation.All);
            return;
        }

        ClearSelection();
        _statusLabel.Text = $"Deleted {pending.Summary}.";
        RefreshOverview();
    }

    private void CancelDeleteConfirmation()
    {
        if (_pendingDeleteSelection is null || _deleteInProgress)
            return;

        _pendingDeleteSelection = null;
        DismissDeleteOverlay();
        _statusLabel.Text = "Deletion cancelled.";
    }

    private void DismissDeleteOverlay()
    {
        _deleteConfirmationOverlay.Dismiss();
        _panelShell.Enabled = true;
    }

    private void BeginInlineSheetRename()
    {
        var selected = SelectedItems().Take(2).ToArray();
        if (selected.Length != 1 || selected[0].Node.Key.Kind != OverviewNodeKind.Sheet)
        {
            return;
        }

        BeginInlineNameRename(selected[0]);
    }

    private void BeginInlineNameRename(HierarchyTreeItem item)
    {
        if (!IsInlineRenameTarget(item) ||
            SelectedItemCount() != 1 ||
            !_selection.Selected.Contains(item.Node.Key))
        {
            return;
        }

        CloseHierarchyDisplayModePicker();
        var kind = item.Node.Key.Kind switch
        {
            OverviewNodeKind.Folder => InlineDraftKind.RenameFolder,
            OverviewNodeKind.Sheet => InlineDraftKind.RenameSheet,
            OverviewNodeKind.LayerState => InlineDraftKind.RenameLayerState,
            OverviewNodeKind.ObjectDisplayState => InlineDraftKind.RenameObjectDisplayState,
            _ => throw new ArgumentOutOfRangeException(),
        };
        var parentFolderId = item.Node.Sheet?.FolderId ??
                             item.Node.AppearanceState?.FolderId ??
                             _overview.Folders.FirstOrDefault(folder => folder.Id == item.Node.Key.Id)?.ParentId ??
                             _overview.RootFolderId ?? Guid.Empty;
        _inlineDraft = new InlineDraft(
            kind,
            item.Node.Key.Id,
            parentFolderId,
            item.Node.Label);
        item.BeginInlineEdit();
        SetInlineEditing(true);
        _statusLabel.Text = kind switch
        {
            InlineDraftKind.RenameFolder => "Rename the folder, then press Return.",
            InlineDraftKind.RenameSheet => "Rename the layout, then press Return. Rhino does not support Undo for this change.",
            _ => "Rename the appearance state, then press Return.",
        };
        Application.Instance.AsyncInvoke(() =>
        {
            var rows = VisibleTreeRows.Flatten(
                    _renderedTreeItems,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .ToArray();
            var row = Array.FindIndex(rows, candidate => candidate.Node.Key == item.Node.Key);
            if (row < 0)
            {
                return;
            }

            _treeGrid.SelectedItem = rows[row];
            _treeGrid.ScrollToRow(row);
            _treeGrid.BeginEdit(row, 0);
        });
    }

    private void RunSelectedSheetCommand(LayoutSheetCommand command)
    {
        var selectedItems = SelectedItems();
        var pageViewId = selectedItems.Count == 1
            ? ResolveSheetPageViewId(selectedItems[0])
            : null;
        if (pageViewId is null)
        {
            return;
        }

        var result = LayoutFoundryUiHost.RunSheetCommand(pageViewId.Value, command);
        _statusLabel.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private void OnOverviewChanged(object? sender, OverviewInvalidationEventArgs eventArgs)
    {
        var application = Application.Instance;
        if (application is null)
        {
            return;
        }

        application.AsyncInvoke(() =>
        {
            lock (_invalidationSyncRoot)
            {
                _pendingInvalidation = _pendingInvalidation is null
                    ? eventArgs.Invalidation
                    : _pendingInvalidation.Merge(eventArgs.Invalidation);
            }

            _invalidationTimer.Stop();
            _invalidationTimer.Start();
        });
    }

    private void OnInvalidationTimer(object? sender, EventArgs eventArgs)
    {
        _invalidationTimer.Stop();
        OverviewInvalidation? invalidation;
        lock (_invalidationSyncRoot)
        {
            invalidation = _pendingInvalidation;
            _pendingInvalidation = null;
        }

        if (invalidation is null)
        {
            return;
        }

        if (invalidation.DocumentRuntimeSerialNumber is { } serial &&
            serial != _overview.DocumentRuntimeSerialNumber)
        {
            return;
        }

        if ((invalidation.Kind & OverviewInvalidationKind.Thumbnails) != 0 &&
            _overview.DocumentRuntimeSerialNumber is { } documentSerial)
        {
            InvalidateThumbnails(documentSerial, invalidation.AffectedEntityIds);
        }

        var requiresHierarchy = (invalidation.Kind & (
            OverviewInvalidationKind.DocumentIdentity |
            OverviewInvalidationKind.Hierarchy |
            OverviewInvalidationKind.Metadata |
            OverviewInvalidationKind.Diagnostics)) != 0;
        if (requiresHierarchy)
        {
            RefreshOverview();
        }
        else if ((invalidation.Kind & OverviewInvalidationKind.Thumbnails) != 0)
        {
            QueueThumbnails();
        }
    }

    private void OnPanelLoaded(object? sender, EventArgs eventArgs)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        ApplyResponsiveLayout();
        LayoutFoundryUiHost.OverviewChanged += OnOverviewChanged;
        LayoutFoundryUiHost.Selection.Changed += OnSharedSelectionChanged;
        _layoutPollTimer.Start();
        RefreshOverview();
        QueueThumbnails();
    }

    private void OnPanelUnloaded(object? sender, EventArgs eventArgs)
    {
        _fullscreenWindow?.Close();
        CloseHierarchyDisplayModePicker();
        _templateRolesOverlay.Dismiss(commit: false);

        if (!_isLoaded)
        {
            return;
        }

        _isLoaded = false;
        _layoutPollTimer.Stop();
        _invalidationTimer.Stop();
        _responsiveTimer.Stop();
        ResetThumbnailCapture();
        LayoutFoundryUiHost.OverviewChanged -= OnOverviewChanged;
        LayoutFoundryUiHost.Selection.Changed -= OnSharedSelectionChanged;
    }

    private void OnLayoutPoll(object? sender, EventArgs eventArgs)
    {
        var identity = LayoutFoundryUiHost.CaptureOverviewIdentity();
        if (!identity.Matches(_overview))
        {
            RefreshOverview();
        }
    }

    private void RefreshOverview()
    {
        _overview = LayoutFoundryUiHost.CaptureOverview();
        if (_documentSerialNumber != _overview.DocumentRuntimeSerialNumber)
        {
            if (_documentSerialNumber is { } previousSerial)
            {
                _thumbnailCache.Invalidate(previousSerial);
                _thumbnailQueue.RemoveDocument(previousSerial);
            }

            ResetThumbnailCapture();
            _selection.Clear();
            if (LayoutFoundryUiHost.Selection.DocumentRuntimeSerialNumber !=
                _overview.DocumentRuntimeSerialNumber)
            {
                LayoutFoundryUiHost.Selection.Clear(_overview.DocumentRuntimeSerialNumber, this);
            }
            _hierarchyExpansion.Clear();
            _documentSerialNumber = _overview.DocumentRuntimeSerialNumber;
        }

        _selection.Prune(Flatten(OverviewTreeBuilder.Build(_overview)).Select(node => node.Key));
        PopulateTree();
    }

    private void PopulateTree()
    {
        var filter = CurrentFilter;
        var renderOverview = OverviewWithInlineDraft();
        var nodes = OverviewTreeSorter.Sort(
            OverviewTreeBuilder.Build(renderOverview, filter),
            _sortProperty,
            _sortDirection);
        var nodeKeys = Flatten(nodes).Select(node => node.Key).ToHashSet();
        _hierarchyExpansion.Prune(nodeKeys);
        var draftKey = _inlineDraft is { } draft
            ? new OverviewNodeKey(
                draft.Kind switch
                {
                    InlineDraftKind.Folder => OverviewNodeKind.Folder,
                    InlineDraftKind.LayerState => OverviewNodeKind.LayerState,
                    InlineDraftKind.ObjectDisplayState => OverviewNodeKind.ObjectDisplayState,
                    _ => OverviewNodeKind.Sheet,
                },
                draft.Id)
            : (OverviewNodeKey?)null;
        var preferredSelection = draftKey is { } visibleDraft && nodeKeys.Contains(visibleDraft)
            ? visibleDraft
            : _selection.Anchor is { } anchor && nodeKeys.Contains(anchor)
                ? anchor
                : _selection.VisibleSelection(nodeKeys).FirstOrDefault();
        var items = nodes
            .Select(node => new HierarchyTreeItem(
                node,
                filter.IsActive,
                preferredSelection,
                _usesMacSafeHierarchy,
                _inlineDraft?.Id,
                _hierarchyExpansion))
            .ToArray();
        _renderedTreeItems = items;
        var visibleItems = Flatten(items).ToDictionary(item => item.Node.Key);
        _sheetItems.Clear();
        foreach (var sheetItem in visibleItems.Values.Where(item =>
                     item.Node.Key.Kind == OverviewNodeKind.Sheet))
        {
            _sheetItems[sheetItem.Node.Key.Id] = sheetItem;
            var key = ThumbnailKey(sheetItem.Node.Key.Id);
            if (_usesMacSafeHierarchy)
            {
                continue;
            }

            if (_thumbnailBitmaps.TryGetValue(key, out var bitmap))
            {
                sheetItem.Thumbnail = bitmap;
            }
            else if (_thumbnailCache.TryGet(key, out var bytes))
            {
                bitmap = new Bitmap(bytes);
                _thumbnailBitmaps[key] = bitmap;
                sheetItem.Thumbnail = bitmap;
            }
        }

        _isPopulatingTree = true;
        try
        {
            _treeGrid.DataStore = new TreeGridItemCollection(items);
            if (preferredSelection != default && visibleItems.TryGetValue(preferredSelection, out var item))
            {
                _treeGrid.SelectedItem = item;
            }

            RestoreVisibleTreeSelection(VisibleTreeRows.Flatten(
                    items,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .ToArray());
        }
        finally
        {
            _isPopulatingTree = false;
        }

        UpdatePresentation();
        QueueThumbnails();
        ApplyFilterProjection();
    }

    private void ApplyFilterProjection()
    {
        var projection = OverviewFilterProjector.Resolve(_overview, CurrentFilter);
        if (!projection.IsActive && !_filterProjection.IsActive)
            return;
        if (projection.IsActive == _filterProjection.IsActive &&
            projection.EmphasizedKeys.SetEquals(_filterProjection.EmphasizedKeys) &&
            projection.MatchingSheetIds.SetEquals(_filterProjection.MatchingSheetIds))
            return;

        _filterProjection = projection;
        _thumbnailView.SetFilter(projection);
        _observerView.SetFilter(projection);
    }

    private void OnSharedSelectionChanged(
        object? sender,
        DocumentSelectionChangedEventArgs eventArgs)
    {
        if (ReferenceEquals(eventArgs.Source, this) ||
            eventArgs.DocumentRuntimeSerialNumber != _overview.DocumentRuntimeSerialNumber)
        {
            return;
        }

        _selection.Replace(eventArgs.Selection, eventArgs.Anchor);
        PopulateTree();
    }

    private DocumentOverview OverviewWithInlineDraft()
    {
        if (_inlineDraft is not { } draft)
        {
            return _overview;
        }

        return draft.Kind switch
        {
            InlineDraftKind.Folder => _overview with
            {
                Folders = _overview.Folders.Append(new FolderOverview(
                    draft.Id,
                    draft.ParentFolderId,
                    draft.Name,
                    int.MaxValue)).ToArray(),
            },
            InlineDraftKind.Sheet => _overview with
            {
                Sheets = _overview.Sheets.Append(new SheetOverview(
                    draft.Id,
                    draft.ParentFolderId,
                    draft.Name,
                    int.MaxValue,
                    [],
                    [])).ToArray(),
            },
            InlineDraftKind.LayerState or InlineDraftKind.ObjectDisplayState => _overview with
            {
                AppearanceStateResources = _overview.AppearanceStates.Append(new AppearanceStateOverview(
                    draft.Id,
                    draft.ParentFolderId,
                    int.MaxValue,
                    draft.Name,
                    draft.Kind == InlineDraftKind.LayerState
                        ? AppearanceStateKind.LayerState
                        : AppearanceStateKind.ObjectDisplayState,
                    0, 0, 0, 0, 0)).ToArray(),
            },
            InlineDraftKind.RenameFolder or InlineDraftKind.RenameSheet or
                InlineDraftKind.RenameLayerState or InlineDraftKind.RenameObjectDisplayState => _overview,
            _ => _overview,
        };
    }

    private void UpdatePresentation()
    {
        var selectedItems = SelectedItems();
        var presentation = OverviewPanelPresentation.Create(
            _overview,
            CurrentFilter,
            selectedItems.Select(item => item.Node.Key));

        var hierarchyContext = selectedItems.Count > 0
            ? presentation.SelectionSummary
            : presentation.ResultSummary;
        UpdateSortHeaders();
        _summaryLabel.Text = string.IsNullOrWhiteSpace(hierarchyContext)
            ? presentation.DocumentSummary
            : $"{presentation.DocumentSummary}  ·  {hierarchyContext}";
        _emptyTitleLabel.Text = presentation.EmptyTitle;
        _emptyDescriptionLabel.Text = presentation.EmptyDescription;

        var showHierarchy = presentation.ContentState == OverviewContentState.Hierarchy;
        if (showHierarchy && !ReferenceEquals(_contentHost.Content, _treeGrid))
        {
            _contentHost.Padding = new Padding(0);
            _contentHost.Content = _treeGrid;
        }
        else if (!showHierarchy && ReferenceEquals(_contentHost.Content, _treeGrid))
        {
            _contentHost.Padding = new Padding(FoundryTheme.Space6);
            _contentHost.Content = CreateEmptyState();
        }

        UpdateSelectionActions(presentation, selectedItems);
    }

    private void OnColumnHeaderClick(GridColumnEventArgs eventArgs)
    {
        var property = ReferenceEquals(eventArgs.Column, _layoutsColumn)
            ? OverviewSortProperty.Name
            : ReferenceEquals(eventArgs.Column, _printColumn)
                ? OverviewSortProperty.Print
                : ReferenceEquals(eventArgs.Column, _templateColumn)
                    ? OverviewSortProperty.Template
                    : ReferenceEquals(eventArgs.Column, _paperColumn)
                        ? OverviewSortProperty.PaperSize
                        : ReferenceEquals(eventArgs.Column, _detailsColumn)
                            ? OverviewSortProperty.DetailCount
                            : ReferenceEquals(eventArgs.Column, _displayModeColumn)
                                ? OverviewSortProperty.DisplayMode
                                : OverviewSortProperty.Status;
        if (_sortProperty == property)
        {
            _sortDirection = _sortDirection == OverviewSortDirection.Ascending
                ? OverviewSortDirection.Descending
                : OverviewSortDirection.Ascending;
        }
        else
        {
            _sortProperty = property;
            _sortDirection = OverviewSortDirection.Ascending;
        }

        PopulateTree();
    }

    private void UpdateSortHeaders()
    {
        _layoutsColumn.HeaderText = SortHeader("Layouts", OverviewSortProperty.Name);
        _printColumn.HeaderText = SortHeader("Print", OverviewSortProperty.Print);
        _templateColumn.HeaderText = SortHeader("Template roles", OverviewSortProperty.Template);
        _paperColumn.HeaderText = SortHeader("Paper size", OverviewSortProperty.PaperSize);
        _detailsColumn.HeaderText = SortHeader("Details", OverviewSortProperty.DetailCount);
        _displayModeColumn.HeaderText = SortHeader("Display mode", OverviewSortProperty.DisplayMode);
        _layersColumn.HeaderText = "Layers";
        _objectModesColumn.HeaderText = "Object modes";
        _statusColumn.HeaderText = SortHeader("Status", OverviewSortProperty.Status);
    }

    private string SortHeader(string label, OverviewSortProperty property) =>
        _sortProperty == property
            ? $"{label} {(_sortDirection == OverviewSortDirection.Ascending ? "▲" : "▼")}"
            : label;

    private void UpdateSelectionActions(
        OverviewPanelPresentation presentation,
        IReadOnlyList<HierarchyTreeItem> selectedItems)
    {
        var selectedKeys = SelectedKeys();
        var selectedSheets = selectedKeys
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Select(key => _overview.Sheets.FirstOrDefault(sheet => sheet.PageViewId == key.Id))
            .Where(sheet => sheet is not null)
            .Cast<SheetOverview>()
            .Take(2)
            .ToArray();
        var selectionCount = selectedKeys.Length;
        var canRename = selectedSheets.Length == 1 && selectionCount == 1;
        var capabilities = LayoutFoundryUiHost.CaptureMutationCapabilities();

        _importButton.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _exportButton.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _projectInfoButton.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _createButton.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        var destinationId = ResolveCreationDestinationFolderId();
        var destinationName = destinationId is { } id ? FolderDestinationName(id) : "Layouts";
        _createButton.ToolTip = $"Create in {destinationName}";
        _manageButton.Enabled = selectionCount > 0;
        _deleteButton.Enabled = selectionCount > 0 &&
                                selectedKeys.All(key => !IsDocumentRootKey(key));
        var renameAvailable = canRename && capabilities.PageRenameUndo.IsSupported;
        _renameActions.Visible = renameAvailable;
        _renameTextBox.Enabled = renameAvailable;
        _renameButton.Enabled = renameAvailable;
        if (renameAvailable && !_renameTextBox.HasFocus)
        {
            _renameTextBox.Text = selectedSheets[0].Name;
        }

        if (selectionCount == 0)
        {
            _statusLabel.Text = string.Empty;
        }
    }

    private void BeginFolderCreation(Guid? explicitParentFolderId = null)
    {
        if (_overview.RootFolderId is not { } rootFolderId)
        {
            _statusLabel.Text = "Open a Rhino document before creating folders.";
            return;
        }

        var destination = explicitParentFolderId ?? ResolveCreationDestinationFolderId() ?? rootFolderId;
        if (_overview.Folders.All(folder => folder.Id != destination))
            destination = rootFolderId;

        _folderDraftId = Guid.NewGuid();
        _folderDraftParentId = destination;
        _folderDraftDestinationLabel.Text = $"New folder in {FolderDestinationName(destination)}";
        _folderDraftTextBox.Text = "New Folder";
        _folderDraftStrip.Visible = true;
        _statusLabel.Text = "Name the new folder, then press Return.";
        Application.Instance.AsyncInvoke(() =>
        {
            _folderDraftTextBox.Focus();
            _folderDraftTextBox.SelectAll();
        });
    }

    private async Task CommitFolderCreationAsync()
    {
        if (_folderDraftId is not { } folderId ||
            _folderDraftParentId is not { } parentFolderId)
            return;

        var name = _folderDraftTextBox.Text.Trim();
        if (name.Length == 0)
        {
            _statusLabel.Text = "A folder name is required.";
            _folderDraftTextBox.Focus();
            return;
        }

        _overview = LayoutFoundryUiHost.CaptureOverview();
        if (_overview.Folders.All(folder => folder.Id != parentFolderId))
        {
            _statusLabel.Text = "The destination folder no longer exists. Cancel and try again.";
            return;
        }

        if (_overview.Folders.Any(folder =>
                folder.ParentId == parentFolderId &&
                string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            _statusLabel.Text = $"A folder named '{name}' already exists there.";
            _folderDraftTextBox.Focus();
            _folderDraftTextBox.SelectAll();
            return;
        }

        _folderDraftCreateButton.Enabled = false;
        _folderDraftCancelButton.Enabled = false;
        _statusLabel.Text = "Creating folder…";
        var result = await LayoutFoundryUiHost.CreateFolderAsync(folderId, parentFolderId, name);
        _folderDraftCreateButton.Enabled = true;
        _folderDraftCancelButton.Enabled = true;
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            _folderDraftTextBox.Focus();
            return;
        }

        CloseFolderDraft();
        _overview = LayoutFoundryUiHost.CaptureOverview();
        var key = new OverviewNodeKey(OverviewNodeKind.Folder, folderId);
        _selection.Replace([key], key);
        LayoutFoundryUiHost.Selection.Replace(
            _overview.DocumentRuntimeSerialNumber,
            [key],
            key,
            this);
        _statusLabel.Text = $"Created folder '{name}'.";
        PopulateTree();
    }

    private void CancelFolderCreation()
    {
        if (_folderDraftId is null) return;
        CloseFolderDraft();
        _statusLabel.Text = string.Empty;
    }

    private void CloseFolderDraft()
    {
        _folderDraftId = null;
        _folderDraftParentId = null;
        _folderDraftTextBox.Text = string.Empty;
        _folderDraftStrip.Visible = false;
    }

    private string FolderDestinationName(Guid folderId)
    {
        if (_overview.RootFolderId == folderId)
        {
            var documentName = Path.GetFileNameWithoutExtension(_overview.DocumentName);
            return string.IsNullOrWhiteSpace(documentName) ? "Layouts" : documentName;
        }

        return _overview.Folders.FirstOrDefault(folder => folder.Id == folderId)?.Name ?? "Layouts";
    }

    private void BeginInlineCreation(InlineDraftKind kind, Guid? explicitParentFolderId = null)
    {
        if (_overview.RootFolderId is not { } rootFolderId)
        {
            _statusLabel.Text = "Open a Rhino document before creating hierarchy items.";
            return;
        }

        if (_treeGrid.IsEditing)
        {
            _inlineDraft = null;
            SetInlineEditing(false);
            _treeGrid.CancelEdit();
        }

        var destination = explicitParentFolderId ?? ResolveCreationDestinationFolderId() ?? rootFolderId;
        if (_overview.Folders.All(folder => folder.Id != destination))
        {
            destination = rootFolderId;
        }

        if (CurrentFilter.IsActive)
        {
            ClearFilter();
        }

        _inlineDraft = new InlineDraft(
            kind,
            Guid.NewGuid(),
            destination,
            kind switch
            {
                InlineDraftKind.Folder => "New Folder",
                InlineDraftKind.Sheet => "New Page",
                InlineDraftKind.LayerState => "New Layer State",
                InlineDraftKind.ObjectDisplayState => "New Object Display State",
                _ => string.Empty,
            });
        SetInlineEditing(true);
        _statusLabel.Text = kind switch
        {
            InlineDraftKind.Folder => "Name the new folder, then press Return.",
            InlineDraftKind.Sheet => "Name the new layout, then press Return.",
            _ => "Name the new appearance state, then press Return.",
        };
        PopulateTree();

        Application.Instance.AsyncInvoke(() =>
        {
            if (_inlineDraft is not { } draft)
            {
                return;
            }

            var item = Flatten(_renderedTreeItems)
                .FirstOrDefault(candidate => candidate.Node.Key.Id == draft.Id);
            if (item is null)
            {
                return;
            }

            _treeGrid.SelectedItem = item;
            var row = VisibleTreeRows.Flatten(
                    _renderedTreeItems,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .TakeWhile(candidate => !ReferenceEquals(candidate, item))
                .Count();
            _treeGrid.ScrollToRow(row);
            _treeGrid.BeginEdit(row, 0);
        });
    }

    private async Task OnTreeCellClickAsync(GridCellMouseEventArgs eventArgs)
    {
        if ((eventArgs.Buttons & MouseButtons.Primary) == 0 ||
            eventArgs.Item is not HierarchyTreeItem item)
        {
            return;
        }

        if (_selectionPreservingCircleInteraction is { } preserved &&
            preserved.Key == item.Node.Key &&
            ReferenceEquals(preserved.Column, eventArgs.GridColumn))
        {
            return;
        }

        if (IsInteractivePropertyColumn(eventArgs.GridColumn) &&
            ConsumeCellInteractionGuard(item, eventArgs.GridColumn))
        {
            _propertyInteractionTargets = [];
            _statusLabel.Text = "Row selected. Click the property again to change it.";
            return;
        }

        if (ReferenceEquals(eventArgs.GridColumn, _paperColumn))
        {
            if (!item.HasSheetTargets)
            {
                _statusLabel.Text = "Paper size applies to folders and layouts, not individual details.";
                return;
            }

            ShowPaperSizeMenu(
                PropertyInteractionTargets(item.Node.Key),
                item.Node.Key,
                eventArgs.Location);
            return;
        }

        if (ReferenceEquals(eventArgs.GridColumn, _displayModeColumn))
        {
            if (!item.HasDetailTargets)
            {
                _statusLabel.Text = "This row does not contain any detail viewports.";
                return;
            }

            BeginDisplayModeEdit(
                PropertyInteractionTargets(item.Node.Key),
                item);
            return;
        }

        if (ReferenceEquals(eventArgs.GridColumn, _templateColumn))
        {
            ShowTemplateRolesPicker(PropertyInteractionTargets(item.Node.Key), eventArgs.Location);
            return;
        }

        if (ReferenceEquals(eventArgs.GridColumn, _layersColumn))
        {
            OpenAppearanceSettings(
                PropertyInteractionTargets(item.Node.Key),
                SelectionInspectorContent.Layers);
            return;
        }

        if (ReferenceEquals(eventArgs.GridColumn, _objectModesColumn))
        {
            OpenAppearanceSettings(
                PropertyInteractionTargets(item.Node.Key),
                SelectionInspectorContent.ObjectModes);
            return;
        }

        if (!ReferenceEquals(eventArgs.GridColumn, _printColumn) || !item.HasSheetTargets)
            return;

        await TogglePrintInclusionAsync(item);
    }

    private async Task TogglePrintInclusionAsync(HierarchyTreeItem item)
    {
        var printTargets = PropertyInteractionTargets(item.Node.Key);
        var include = !item.AllPrintIncluded;
        _statusLabel.Text = include
            ? "Enabling layouts for printing…"
            : "Disabling layouts for printing…";
        var result = await LayoutFoundryUiHost.SetPrintInclusionAsync(printTargets, include);
        _statusLabel.Text = result.Succeeded
            ? include
                ? "Enabled for printing."
                : "Disabled from printing."
            : DiagnosticMessage(result);
        RefreshOverview();
    }

    private void ShowPaperSizeMenu(
        IReadOnlyList<OverviewNodeKey> targets,
        OverviewNodeKey source,
        PointF location)
    {
        var custom = new ButtonMenuItem { Text = "Custom…" };
        custom.Click += (_, _) => Application.Instance.AsyncInvoke(
            async () => await SetCustomPaperSizeAsync(targets, source));
        var menuItems = new List<MenuItem>
        {
            custom,
            new SeparatorMenuItem(),
        };
        foreach (var choice in PaperSizeChoices)
        {
            var capturedChoice = choice;
            var menuItem = new ButtonMenuItem { Text = choice.Label };
            menuItem.Click += (_, _) => Application.Instance.AsyncInvoke(
                async () => await SetPaperSizeAsync(targets, capturedChoice));
            menuItems.Add(menuItem);
        }

        new ContextMenu(menuItems).Show(_treeGrid, location);
    }

    private void BeginDisplayModeEdit(
        IReadOnlyList<OverviewNodeKey> targets,
        HierarchyTreeItem item)
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        var modes = snapshot?.DisplayModes
            .Where(pair => pair.Key != Guid.Empty && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (modes.Length == 0)
        {
            _statusLabel.Text = "No Rhino display modes are available.";
            return;
        }

        _hierarchyDisplayModes = modes
            .GroupBy(mode => mode.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Key, StringComparer.OrdinalIgnoreCase);
        _hierarchyDisplayModeTargets = targets.Distinct().ToArray();
        _hierarchyDisplayModeEditingKey = item.Node.Key;
        _activeHierarchyDisplayModePicker = null;
        _treeGrid.ReloadItem(item, reloadChildren: false);

        Application.Instance.AsyncInvoke(() =>
        {
            if (_hierarchyDisplayModeEditingKey == item.Node.Key &&
                _activeHierarchyDisplayModePicker is { } picker)
            {
                picker.OpenResults();
            }
        });
    }

    private async Task CommitHierarchyDisplayModeAsync()
    {
        var modeName = _activeHierarchyDisplayModePicker?.Text.Trim() ?? string.Empty;
        if (!_hierarchyDisplayModes.TryGetValue(modeName, out var modeId) ||
            _hierarchyDisplayModeTargets.Count == 0)
        {
            return;
        }

        var targets = _hierarchyDisplayModeTargets.ToArray();
        CloseHierarchyDisplayModePicker();
        await SetDisplayModeAsync(targets, modeId, modeName);
    }

    private void CloseHierarchyDisplayModePicker()
    {
        var editingKey = _hierarchyDisplayModeEditingKey;
        var picker = _activeHierarchyDisplayModePicker;
        _hierarchyDisplayModeEditingKey = null;
        _activeHierarchyDisplayModePicker = null;
        picker?.CloseResults();
        _hierarchyDisplayModeTargets = [];
        _hierarchyDisplayModes.Clear();

        if (editingKey is { } key)
        {
            var item = Flatten(_renderedTreeItems)
                .FirstOrDefault(candidate => candidate.Node.Key == key);
            if (item is not null)
                _treeGrid.ReloadItem(item, reloadChildren: false);
        }
    }

    private async Task SetPaperSizeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        PaperSizeChoice choice)
    {
        _statusLabel.Text = $"Setting {choice.Label}…";
        var result = await LayoutFoundryUiHost.SetPaperSizeAsync(
            targets,
            choice.Width,
            choice.Height,
            choice.UnitSystem);
        _statusLabel.Text = result.Succeeded ? "Layout properties updated." : DiagnosticMessage(result);
        RefreshOverview();
    }

    private async Task SetCustomPaperSizeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        OverviewNodeKey source)
    {
        var initial = FirstTargetSheet(source);
        var dialog = new CustomPaperSizeDialog(
            initial?.PageWidth ?? 420,
            initial?.PageHeight ?? 297,
            initial?.PageUnitSystem ?? "Millimeters");
        dialog.ShowModal(this);
        if (!dialog.Accepted)
        {
            return;
        }

        _statusLabel.Text = "Setting custom paper size…";
        var result = await LayoutFoundryUiHost.SetPaperSizeAsync(
            targets,
            dialog.PaperWidth,
            dialog.PaperHeight,
            dialog.UnitSystem);
        _statusLabel.Text = result.Succeeded ? "Layout properties updated." : DiagnosticMessage(result);
        RefreshOverview();
    }

    private async Task SetDisplayModeAsync(
        IReadOnlyList<OverviewNodeKey> targets,
        Guid modeId,
        string modeName)
    {
        _statusLabel.Text = $"Setting {modeName}…";
        var result = await LayoutFoundryUiHost.SetDisplayModeAsync(targets, modeId);
        _statusLabel.Text = result.Succeeded ? "Layout properties updated." : DiagnosticMessage(result);
        RefreshOverview();
    }

    private SheetOverview? FirstTargetSheet(OverviewNodeKey target)
    {
        if (target.Kind == OverviewNodeKind.Sheet)
        {
            return _overview.Sheets.FirstOrDefault(sheet => sheet.PageViewId == target.Id);
        }

        if (target.Kind == OverviewNodeKind.Detail)
        {
            return _overview.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == target.Id));
        }

        var folderIds = DescendantFolderIds(target.Id).ToHashSet();
        return _overview.Sheets
            .OrderBy(sheet => sheet.Order)
            .FirstOrDefault(sheet => folderIds.Contains(sheet.FolderId));
    }

    private IEnumerable<Guid> DescendantFolderIds(Guid folderId)
    {
        yield return folderId;
        foreach (var child in _overview.Folders.Where(folder => folder.ParentId == folderId))
        {
            foreach (var descendant in DescendantFolderIds(child.Id))
            {
                yield return descendant;
            }
        }
    }

    private async Task OnTreeCellEditedAsync(GridViewCellEventArgs eventArgs)
    {
        if (_inlineDraft is not null)
        {
            await CommitInlineDraftAsync(eventArgs);
        }
    }

    private async Task CommitInlineDraftAsync(GridViewCellEventArgs eventArgs)
    {
        if (_inlineDraft is not { } draft ||
            eventArgs.Item is not HierarchyTreeItem { IsInlineDraft: true } item ||
            item.Node.Key.Id != draft.Id)
        {
            return;
        }

        var name = item.DisplayText.Trim();
        if (name.Length == 0)
        {
            _statusLabel.Text = "A name is required.";
            BeginEditingDraftAgain();
            return;
        }

        draft.Name = name;
        _statusLabel.Text = draft.Kind switch
        {
            InlineDraftKind.Folder => "Creating folder…",
            InlineDraftKind.Sheet => "Creating layout…",
            InlineDraftKind.RenameFolder => "Renaming folder…",
            InlineDraftKind.RenameSheet => "Renaming layout…",
            InlineDraftKind.LayerState or InlineDraftKind.ObjectDisplayState => "Creating appearance state…",
            InlineDraftKind.RenameLayerState or InlineDraftKind.RenameObjectDisplayState => "Renaming appearance state…",
            _ => string.Empty,
        };
        var result = draft.Kind switch
        {
            InlineDraftKind.Folder => await LayoutFoundryUiHost.CreateFolderAsync(
                draft.Id,
                draft.ParentFolderId,
                name),
            InlineDraftKind.Sheet => await LayoutFoundryUiHost.CreateSheetAsync(
                draft.ParentFolderId,
                name),
            InlineDraftKind.RenameFolder => await LayoutFoundryUiHost.RenameFolderAsync(
                draft.Id,
                draft.OriginalName,
                name),
            InlineDraftKind.RenameSheet => ToOperationResult(
                LayoutFoundryUiHost.RenameSheetDirect(draft.Id, name)),
            InlineDraftKind.LayerState => await LayoutFoundryUiHost.CreateAppearanceStateAsync(
                draft.ParentFolderId, name, AppearanceStateKind.LayerState),
            InlineDraftKind.ObjectDisplayState => await LayoutFoundryUiHost.CreateAppearanceStateAsync(
                draft.ParentFolderId, name, AppearanceStateKind.ObjectDisplayState),
            InlineDraftKind.RenameLayerState or InlineDraftKind.RenameObjectDisplayState =>
                await LayoutFoundryUiHost.UpdateAppearanceStateAsync(draft.Id, name),
            _ => throw new ArgumentOutOfRangeException(),
        };
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            BeginEditingDraftAgain();
            return;
        }

        _inlineDraft = null;
        SetInlineEditing(false);
        _overview = LayoutFoundryUiHost.CaptureOverview();
        var createdKey = draft.Kind == InlineDraftKind.Folder
            ? new OverviewNodeKey(OverviewNodeKind.Folder, draft.Id)
            : draft.Kind == InlineDraftKind.RenameFolder
                ? new OverviewNodeKey(OverviewNodeKind.Folder, draft.Id)
            : draft.Kind == InlineDraftKind.RenameSheet
                ? new OverviewNodeKey(OverviewNodeKind.Sheet, draft.Id)
            : draft.Kind is InlineDraftKind.RenameLayerState or InlineDraftKind.RenameObjectDisplayState
                ? new OverviewNodeKey(
                    draft.Kind == InlineDraftKind.RenameLayerState
                        ? OverviewNodeKind.LayerState
                        : OverviewNodeKind.ObjectDisplayState,
                    draft.Id)
            : draft.Kind is InlineDraftKind.LayerState or InlineDraftKind.ObjectDisplayState
                ? _overview.AppearanceStates.FirstOrDefault(state =>
                    state.FolderId == draft.ParentFolderId &&
                    string.Equals(state.Name, name, StringComparison.Ordinal) &&
                    state.Kind == (draft.Kind == InlineDraftKind.LayerState
                        ? AppearanceStateKind.LayerState
                        : AppearanceStateKind.ObjectDisplayState)) is { } state
                    ? new OverviewNodeKey(
                        state.Kind == AppearanceStateKind.LayerState
                            ? OverviewNodeKind.LayerState
                            : OverviewNodeKind.ObjectDisplayState,
                        state.Id)
                    : (OverviewNodeKey?)null
                : _overview.Sheets
                    .Where(sheet => sheet.FolderId == draft.ParentFolderId)
                    .FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal)) is { } sheet
                    ? new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId)
                    : (OverviewNodeKey?)null;
        if (createdKey is { } key)
        {
            _selection.Replace([key], key);
        }

        _statusLabel.Text = draft.Kind switch
        {
            InlineDraftKind.Folder => $"Created folder '{name}'.",
            InlineDraftKind.Sheet => $"Created layout '{name}'. Rhino does not support Undo for layout creation.",
            InlineDraftKind.RenameFolder => $"Renamed folder to '{name}'.",
            InlineDraftKind.RenameSheet => $"Renamed layout to '{name}'. Rhino does not support Undo for this change.",
            InlineDraftKind.LayerState => $"Created layer state '{name}'.",
            InlineDraftKind.ObjectDisplayState => $"Created object display state '{name}'.",
            InlineDraftKind.RenameLayerState or InlineDraftKind.RenameObjectDisplayState =>
                $"Renamed appearance state to '{name}'.",
            _ => string.Empty,
        };
        PopulateTree();
    }

    private static OperationResult ToOperationResult(OverviewNavigationResult result)
    {
        return result.Succeeded
            ? new OperationResult(true, [])
            : new OperationResult(
                false,
                [new RhinoLayoutFoundry.Core.Diagnostics.Diagnostic(
                    "layout.rename_failed",
                    RhinoLayoutFoundry.Core.Diagnostics.DiagnosticSeverity.Error,
                    result.Message)]);
    }

    private void BeginEditingDraftAgain()
    {
        Application.Instance.AsyncInvoke(() =>
        {
            if (_inlineDraft is not { } draft)
            {
                return;
            }

            if (!IsRenameDraft(draft.Kind))
            {
                PopulateTree();
            }

            var rows = VisibleTreeRows.Flatten(
                    _renderedTreeItems,
                    candidate => candidate.Children.OfType<HierarchyTreeItem>(),
                    candidate => candidate.Expanded)
                .ToArray();
            var row = Array.FindIndex(rows, candidate => candidate.Node.Key.Id == draft.Id);
            if (row >= 0)
            {
                rows[row].BeginInlineEdit();
                _treeGrid.SelectedItem = rows[row];
                _treeGrid.BeginEdit(row, 0);
            }
        });
    }

    private static bool IsRenameDraft(InlineDraftKind kind) =>
        kind is InlineDraftKind.RenameFolder or
            InlineDraftKind.RenameSheet or
            InlineDraftKind.RenameLayerState or
            InlineDraftKind.RenameObjectDisplayState;

    private void UpdateContextMenuActions()
    {
        var folder = SelectedFolderItem();
        var selectedItems = SelectedItems();
        var selectionCount = selectedItems.Count;
        var isDocumentContext = selectedItems is [{ Node.IsDocumentRoot: true }];
        var selectedSheet = selectedItems.Count == 1
            ? ResolveSheetPageViewId(selectedItems[0])
            : null;
        var isFolderContext = folder is not null && !isDocumentContext;
        var isSheetContext = selectedSheet is not null;
        var isRootContext = selectedItems.Count == 0 || isDocumentContext;
        var hasLayoutPropertyTargets = selectedItems.Any(item => item.HasSheetTargets);
        _contextDestinationFolderId = ResolveCreationDestinationFolderId();
        _contextPrintFolderId = isFolderContext ? folder?.Node.Key.Id : null;
        var printScope = LayoutPrintScopeResolver.Resolve(_overview, _contextPrintFolderId);
        var destinationName = _overview.Folders
            .FirstOrDefault(candidate => candidate.Id == _contextDestinationFolderId)?.Name;
        _setCurrentMenuItem.Visible = isSheetContext;
        _setCurrentMenuItem.Enabled = isSheetContext;
        _newFolderMenuItem.Visible = isFolderContext || isRootContext;
        _newFolderMenuItem.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _newPageMenuItem.Visible = true;
        _newPageMenuItem.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _newLayerStateMenuItem.Visible = isFolderContext || isRootContext;
        _newLayerStateMenuItem.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _newObjectStateMenuItem.Visible = isFolderContext || isRootContext;
        _newObjectStateMenuItem.Enabled = _overview.DocumentRuntimeSerialNumber is not null;
        _newFolderMenuItem.Text = destinationName is null || _contextDestinationFolderId == _overview.RootFolderId
            ? "New Folder"
            : $"New Folder in {destinationName}";
        _newPageMenuItem.Text = destinationName is null || _contextDestinationFolderId == _overview.RootFolderId
            ? "New Layout…"
            : $"New Layout in {destinationName}…";
        _newLayerStateMenuItem.Text = destinationName is null || _contextDestinationFolderId == _overview.RootFolderId
            ? "New Layer State"
            : $"New Layer State in {destinationName}";
        _newObjectStateMenuItem.Text = destinationName is null || _contextDestinationFolderId == _overview.RootFolderId
            ? "New Object Display State"
            : $"New Object Display State in {destinationName}";
        _duplicateSelectionMenuItem.Visible = selectionCount > 0 && !isDocumentContext;
        _duplicateSelectionMenuItem.Enabled = selectionCount > 0 && !isDocumentContext;
        _copySelectionMenuItem.Visible = selectionCount > 0 && !isDocumentContext;
        _copySelectionMenuItem.Enabled = selectionCount > 0 && !isDocumentContext;
        _pasteSelectionMenuItem.Visible = true;
        _pasteSelectionMenuItem.Enabled = HierarchyClipboard.CanPasteCurrentDocument();
        _deleteSelectionMenuItem.Visible = selectionCount > 0 && !isDocumentContext;
        _deleteSelectionMenuItem.Enabled = selectionCount > 0 && !isDocumentContext;
        _duplicateSelectionMenuItem.Text = folder is not null
            ? "Duplicate Folder"
            : isSheetContext
                ? "Duplicate Layout"
                : $"Duplicate {selectionCount} Items";
        _deleteSelectionMenuItem.Text = folder is not null
            ? "Delete Folder…"
            : isSheetContext
                ? "Delete Layout…"
                : $"Delete {selectionCount} Items…";
        _renamePageMenuItem.Visible = isSheetContext;
        _renamePageMenuItem.Enabled = isSheetContext;
        _newDetailMenuItem.Visible = isSheetContext;
        _newDetailMenuItem.Enabled = isSheetContext;
        _printPageMenuItem.Visible = isSheetContext;
        _printPageMenuItem.Enabled = isSheetContext;
        _printScopeMenuItem.Visible = isFolderContext || isRootContext;
        _printScopeMenuItem.Enabled = printScope.Exists && printScope.HasSheets;
        _printScopeMenuItem.Text = isFolderContext ? "Print Folder…" : "Print Enabled…";
        var hasAppearanceStateTarget = selectedItems.Count == 1 &&
            selectedItems[0].Node.Key.Kind is OverviewNodeKind.LayerState or OverviewNodeKind.ObjectDisplayState;
        _propertiesPageMenuItem.Visible = hasLayoutPropertyTargets || hasAppearanceStateTarget;
        _propertiesPageMenuItem.Enabled = hasLayoutPropertyTargets || hasAppearanceStateTarget;
        _propertiesPageMenuItem.Text = hasAppearanceStateTarget ? "Edit State…" : "Layout Properties…";
        _renameFolderMenuItem.Enabled = folder is not null && !isDocumentContext;
        _renameFolderMenuItem.Visible = folder is not null && !isDocumentContext;
    }

    private async Task RenameSelectedFolderAsync()
    {
        var folder = SelectedFolderItem();
        if (folder is null)
        {
            return;
        }

        var dialog = new RenameFolderDialog(folder.Node.Label);
        dialog.ShowModal(this);
        if (!dialog.Accepted)
        {
            return;
        }

        _statusLabel.Text = "Renaming folder…";
        var result = await LayoutFoundryUiHost.RenameFolderAsync(
            folder.Node.Key.Id,
            folder.Node.Label,
            dialog.FolderName);
        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        var folderKey = folder.Node.Key;
        _selection.Replace([folderKey], folderKey);
        _statusLabel.Text = $"Renamed folder to '{dialog.FolderName}'.";
        RefreshOverview();
    }


    private void OnTreeMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        var cell = _treeGrid.GetCellAt(eventArgs.Location);
        var item = cell.Item as HierarchyTreeItem;
        var column = cell.Column;
        if (_hierarchyDisplayModeEditingKey is { } editingKey &&
            (item?.Node.Key != editingKey || !ReferenceEquals(column, _displayModeColumn)))
        {
            CloseHierarchyDisplayModePicker();
        }

        _cellInteractionGuard = null;
        _selectionPreservingCircleInteraction = null;
        _propertyInteractionTargets = [];
        if ((eventArgs.Buttons & MouseButtons.Primary) != 0 &&
            eventArgs.Modifiers == Keys.None &&
            item is not null &&
            column is not null &&
            IsInteractivePropertyColumn(column))
        {
            var selected = _selection.Selected.ToArray();
            _propertyInteractionTargets = selected.Contains(item.Node.Key)
                ? selected
                : [item.Node.Key];

            if (selected.Length > 1 &&
                selected.Contains(item.Node.Key) &&
                IsSelectionPreservingCircleColumn(column))
            {
                var interaction = new CellInteractionGuard(item.Node.Key, column);
                _selectionPreservingCircleInteraction = interaction;
                eventArgs.Handled = true;
                ResetPendingDrag();
                Application.Instance.AsyncInvoke(async () =>
                {
                    if (!ReferenceEquals(_selectionPreservingCircleInteraction, interaction))
                        return;

                    _selectionPreservingCircleInteraction = null;
                    if (ReferenceEquals(column, _templateColumn))
                        ShowTemplateRolesPicker(
                            PropertyInteractionTargets(item.Node.Key),
                            eventArgs.Location);
                    else if (item.HasSheetTargets)
                        await TogglePrintInclusionAsync(item);
                });
                return;
            }
        }

        if ((eventArgs.Buttons & MouseButtons.Primary) != 0 &&
            item is not null &&
            !item.IsInlineDraft &&
            column is not null &&
            IsInteractivePropertyColumn(column) &&
            (eventArgs.Modifiers != Keys.None ||
             !_selection.Selected.Contains(item.Node.Key)))
        {
            var guard = new CellInteractionGuard(item.Node.Key, column);
            _cellInteractionGuard = guard;
            Application.Instance.AsyncInvoke(() =>
            {
                if (ReferenceEquals(_cellInteractionGuard, guard))
                {
                    _cellInteractionGuard = null;
                }
            });
        }

        if ((eventArgs.Buttons & MouseButtons.Alternate) != 0)
        {
            if (item is null)
            {
                ClearSelection();
            }
            else if (!SelectedItems().Contains(item))
            {
                _treeGrid.SelectedItem = item;
            }

            ResetPendingDrag();
            return;
        }

        if ((eventArgs.Buttons & MouseButtons.Primary) == 0 ||
            eventArgs.Modifiers != Keys.None ||
            item is null ||
            item.IsInlineDraft ||
            item.Node.IsDocumentRoot)
        {
            ResetPendingDrag();
            return;
        }

        if (!SelectedItems().Contains(item))
        {
            _treeGrid.SelectedItem = item;
        }

        _dragStart = eventArgs.Location;
        _dragSourceItem = item;
    }

    private bool IsInteractivePropertyColumn(GridColumn column) =>
        ReferenceEquals(column, _printColumn) ||
        ReferenceEquals(column, _templateColumn) ||
        ReferenceEquals(column, _paperColumn) ||
        ReferenceEquals(column, _displayModeColumn) ||
        ReferenceEquals(column, _layersColumn) ||
        ReferenceEquals(column, _objectModesColumn);

    private bool IsInlineRenameTarget(HierarchyTreeItem item, GridColumn? column = null) =>
        (column is null || ReferenceEquals(column, _layoutsColumn)) &&
        !item.IsInlineDraft &&
        !item.Node.IsDocumentRoot &&
        item.Node.Key.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet or
            OverviewNodeKind.LayerState or OverviewNodeKind.ObjectDisplayState;

    private bool IsSelectionPreservingCircleColumn(GridColumn column) =>
        ReferenceEquals(column, _printColumn) ||
        ReferenceEquals(column, _templateColumn);

    private bool ConsumeCellInteractionGuard(HierarchyTreeItem item, GridColumn column)
    {
        if (_cellInteractionGuard is not { } guard ||
            guard.Key != item.Node.Key ||
            !ReferenceEquals(guard.Column, column))
        {
            return false;
        }

        _cellInteractionGuard = null;
        return true;
    }

    private IReadOnlyList<OverviewNodeKey> PropertyInteractionTargets(OverviewNodeKey source)
    {
        var targets = _propertyInteractionTargets.Contains(source)
            ? _propertyInteractionTargets.Distinct().ToArray()
            : [source];
        _propertyInteractionTargets = [];
        return targets;
    }

    private void OnTreeMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        if (_dragInProgress ||
            _dragStart is not { } start ||
            _dragSourceItem is null ||
            (eventArgs.Buttons & MouseButtons.Primary) == 0)
        {
            return;
        }

        var deltaX = eventArgs.Location.X - start.X;
        var deltaY = eventArgs.Location.Y - start.Y;
        if ((deltaX * deltaX) + (deltaY * deltaY) < 36)
        {
            return;
        }

        var selectedItems = SelectedItems();
        var sources = selectedItems.Contains(_dragSourceItem)
            ? selectedItems
            : [_dragSourceItem];
        _dragSourceKeys = sources.Select(item => item.Node.Key).Distinct().ToArray();
        _dragInProgress = true;
        _statusLabel.Text = "Drop on a folder, or on empty hierarchy space to move to the root.";
        var dragData = new DataObject();
        dragData.SetString("move", InternalHierarchyDragType);
        _treeGrid.DoDragDrop(dragData, DragEffects.Move);
    }

    private void OnTreeDragOver(object? sender, DragEventArgs eventArgs)
    {
        if (!_dragInProgress || !eventArgs.Data.Contains(InternalHierarchyDragType))
        {
            eventArgs.Effects = DragEffects.None;
            return;
        }

        var dragInfo = _treeGrid.GetDragInfo(eventArgs);
        dragInfo.RestrictToOver();
        var target = dragInfo.Item as HierarchyTreeItem;
        eventArgs.Effects = target is null || target.Node.Key.Kind == OverviewNodeKind.Folder
            ? DragEffects.Move
            : DragEffects.None;
    }

    private async Task CompleteInternalDragAsync(DragEventArgs eventArgs)
    {
        if (!_dragInProgress ||
            !eventArgs.Data.Contains(InternalHierarchyDragType) ||
            _dragSourceItem is not { } dragSource)
        {
            eventArgs.Effects = DragEffects.None;
            ResetPendingDrag();
            return;
        }

        var target = _treeGrid.GetDragInfo(eventArgs).Item as HierarchyTreeItem;
        var destinationFolderId = target is null
            ? _overview.RootFolderId
            : target.Node.Key.Kind == OverviewNodeKind.Folder
                ? target.Node.Key.Id
                : null;
        var sourceKeys = _dragSourceKeys.Count > 0
            ? _dragSourceKeys
            : [dragSource.Node.Key];
        ResetPendingDrag();

        if (destinationFolderId is null)
        {
            eventArgs.Effects = DragEffects.None;
            _statusLabel.Text = "Move cancelled. Drop on a folder or empty hierarchy space.";
            return;
        }

        eventArgs.Effects = DragEffects.Move;

        var destinationName = _overview.Folders
            .FirstOrDefault(folder => folder.Id == destinationFolderId.Value)?.Name ?? "Layouts";
        var folderIds = sourceKeys
            .Where(key => key.Kind == OverviewNodeKind.Folder)
            .Select(key => key.Id)
            .Distinct()
            .ToArray();
        OperationResult result;
        OverviewNodeKey[] movedKeys;
        string noun;
        if (folderIds.Length > 0)
        {
            if (sourceKeys.Any(key => key.Kind != OverviewNodeKind.Folder))
            {
                _statusLabel.Text = "Move folders and layouts separately.";
                return;
            }

            _statusLabel.Text = $"Moving {folderIds.Length} folder{(folderIds.Length == 1 ? string.Empty : "s")} to {destinationName}…";
            result = await LayoutFoundryUiHost.MoveFoldersAsync(destinationFolderId.Value, folderIds);
            movedKeys = folderIds.Select(id => new OverviewNodeKey(OverviewNodeKind.Folder, id)).ToArray();
            noun = folderIds.Length == 1 ? "folder" : "folders";
        }
        else
        {
            var sheetIds = sourceKeys
                .Select(ResolveSheetPageViewId)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            if (sheetIds.Length == 0)
            {
                _statusLabel.Text = "Nothing was moved.";
                return;
            }

            _statusLabel.Text = $"Moving {sheetIds.Length} layout{(sheetIds.Length == 1 ? string.Empty : "s")} to {destinationName}…";
            result = await LayoutFoundryUiHost.MoveSheetsAsync(destinationFolderId.Value, sheetIds);
            movedKeys = sheetIds.Select(id => new OverviewNodeKey(OverviewNodeKind.Sheet, id)).ToArray();
            noun = sheetIds.Length == 1 ? "layout" : "layouts";
        }

        if (!result.Succeeded)
        {
            _statusLabel.Text = DiagnosticMessage(result);
            return;
        }

        _selection.Replace(movedKeys, movedKeys.FirstOrDefault());
        _statusLabel.Text = $"Moved {movedKeys.Length} {noun} to {destinationName}.";
        RefreshOverview();
    }

    private Guid? ResolveSheetPageViewId(HierarchyTreeItem item)
    {
        return ResolveSheetPageViewId(item.Node.Key);
    }

    private Guid? ResolveSheetPageViewId(OverviewNodeKey key)
    {
        if (key.Kind == OverviewNodeKind.Sheet)
        {
            return key.Id;
        }

        if (key.Kind != OverviewNodeKind.Detail)
        {
            return null;
        }

        return _overview.Sheets
            .FirstOrDefault(sheet => sheet.Details.Any(detail =>
                detail.DetailViewportId == key.Id))?
            .PageViewId;
    }

    private HierarchyTreeItem? SelectedFolderItem()
    {
        var selected = SelectedItems().Take(2).ToArray();
        return selected.Length == 1 && selected[0].Node.Key.Kind == OverviewNodeKind.Folder
            ? selected[0]
            : null;
    }

    private Guid? ResolveCreationDestinationFolderId()
    {
        return FolderCreationDestination.Resolve(_overview, SelectedKeys())?.ParentFolderId;
    }

    private void ResetPendingDrag()
    {
        _dragStart = null;
        _dragSourceItem = null;
        _dragSourceKeys = [];
        _dragInProgress = false;
    }

    private static string DiagnosticMessage(OperationResult result)
    {
        return string.Join(" ", result.Diagnostics.Select(item => item.Message));
    }

    private void OpenBatchProperties()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
        {
            _statusLabel.Text = "The active Rhino document is unavailable.";
            return;
        }
        var selection = SelectedKeys().Distinct().ToArray();
        if (selection.Length == 0)
        {
            _statusLabel.Text = "Select a folder, layout, or detail first.";
            return;
        }

        if (selection.Any(key => key.Kind == OverviewNodeKind.Detail))
        {
            var detailIds = BatchTargetResolver.ResolveDetailIds(snapshot, selection);
            if (detailIds.Count == 0)
            {
                _statusLabel.Text = "The selection does not contain any detail viewports.";
                return;
            }
            var detailDialog = new DetailPropertiesDialog(snapshot, detailIds);
            detailDialog.ShowModal(this);
            if (detailDialog.Succeeded)
            {
                _statusLabel.Text = $"Updated {detailIds.Count} detail{(detailIds.Count == 1 ? string.Empty : "s")}.";
                RefreshOverview();
            }
            return;
        }

        var targets = BatchTargetResolver.Resolve(snapshot, selection);
        if (targets.Count == 0)
        {
            _statusLabel.Text = "The selection does not contain any layouts.";
            return;
        }
        var dialog = new BatchCreateLayoutsDialog(snapshot, targets);
        dialog.ShowModal(this);
        if (dialog.Succeeded)
        {
            _statusLabel.Text = $"Updated {targets.Count} layout{(targets.Count == 1 ? string.Empty : "s")}.";
            RefreshOverview();
        }
    }

    private void OpenSelectedProperties()
    {
        var selection = SelectedKeys().Distinct().ToArray();
        if (selection.Length != 1 || selection[0].Kind is not
                (OverviewNodeKind.LayerState or OverviewNodeKind.ObjectDisplayState))
        {
            OpenBatchProperties();
            return;
        }

        OpenAppearanceSettings(
            selection,
            selection[0].Kind == OverviewNodeKind.LayerState
                ? SelectionInspectorContent.Layers
                : SelectionInspectorContent.ObjectModes);
    }

    private void OpenAppearanceSettings(
        IReadOnlyList<OverviewNodeKey> selection,
        SelectionInspectorContent contentMode)
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
        {
            _statusLabel.Text = "The active Rhino document is unavailable.";
            return;
        }
        var targets = selection.Distinct().ToArray();
        if (targets.Length == 1 && targets[0].Kind is
                OverviewNodeKind.LayerState or OverviewNodeKind.ObjectDisplayState)
        {
            var state = snapshot.AppearanceStates.FirstOrDefault(item => item.Id == targets[0].Id);
            if (state is null)
            {
                _statusLabel.Text = "The selected appearance state is no longer available.";
                return;
            }
            if (state.Kind == AppearanceStateKind.LayerState !=
                (contentMode == SelectionInspectorContent.Layers))
            {
                _statusLabel.Text = state.Kind == AppearanceStateKind.LayerState
                    ? "Edit this resource from the Layers column."
                    : "Edit this resource from the Object modes column.";
                return;
            }
            var stateDialog = new AppearanceStateEditorDialog(snapshot, state);
            stateDialog.ShowModal(this);
            if (stateDialog.Changed)
            {
                _statusLabel.Text = $"Updated '{state.Name}'.";
                RefreshOverview();
            }
            return;
        }
        if (targets.Length == 0)
        {
            _statusLabel.Text = "Select a folder, layout, or detail first.";
            return;
        }
        var dialog = new SelectionInspectorDialog(snapshot, targets, contentMode);
        dialog.ShowModal(this);
        if (dialog.Changed)
        {
            _statusLabel.Text = $"Updated {targets.Length} selected item{(targets.Length == 1 ? string.Empty : "s")}.";
            RefreshOverview();
        }
    }

    private void ShowTemplateRolesPicker(
        IReadOnlyList<OverviewNodeKey> selection,
        PointF hierarchyLocation)
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
        {
            _statusLabel.Text = "The active Rhino document is unavailable.";
            return;
        }
        var targets = selection.Distinct().Where(key => key.Kind is
            OverviewNodeKind.Folder or OverviewNodeKind.Sheet or OverviewNodeKind.Detail).ToArray();
        if (targets.Length == 0)
        {
            _statusLabel.Text = "Template roles apply to folders, layouts, and details.";
            return;
        }

        var initial = targets.ToDictionary(key => key, key =>
        {
            var scope = ToHierarchyScope(key);
            return snapshot.TemplateRegistrations.LastOrDefault(item => item.Source == scope)
                ?.Capabilities ?? TemplateCapability.None;
        });
        var allowed = targets.Select(key => TemplateCapabilityPolicy.AllowedFor(
                ToHierarchyScope(key).Kind))
            .Aggregate((left, right) => left & right);
        var screenPoint = _treeGrid.PointToScreen(hierarchyLocation);
        var overlayPoint = _panelOverlayHost.PointFromScreen(screenPoint);
        _templateRolesOverlay.ShowPicker(
            initial,
            allowed,
            new Point((int)Math.Round(overlayPoint.X), (int)Math.Round(overlayPoint.Y + _treeGrid.RowHeight)));
    }

    private async Task CommitTemplateRolesAsync(
        IReadOnlyDictionary<OverviewNodeKey, TemplateCapability> values)
    {
        if (values.Count == 0) return;
        _statusLabel.Text = "Updating template roles…";
        foreach (var group in values.GroupBy(pair => pair.Value))
        {
            var result = await LayoutFoundryUiHost.SetTemplateCapabilitiesAsync(
                group.Select(pair => pair.Key).ToArray(),
                group.Key);
            if (result.Succeeded) continue;
            _statusLabel.Text = DiagnosticMessage(result);
            RefreshOverview();
            return;
        }
        _statusLabel.Text = "Template roles updated.";
        RefreshOverview();
    }

    private static HierarchyScope ToHierarchyScope(OverviewNodeKey key) => new(
        key.Kind switch
        {
            OverviewNodeKind.Folder => HierarchyScopeKind.Folder,
            OverviewNodeKind.Sheet => HierarchyScopeKind.Sheet,
            OverviewNodeKind.Detail => HierarchyScopeKind.Detail,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        },
        key.Id);

    private void OpenCreateLayouts(Guid? preferredFolderId)
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null)
        {
            _statusLabel.Text = "Open a Rhino document before creating layouts.";
            return;
        }
        var dialog = new BatchCreateLayoutsDialog(snapshot, preferredFolderId);
        dialog.ShowModal(this);
        if (dialog.Succeeded)
        {
            _statusLabel.Text = $"Created {dialog.CreatedCount} layout{(dialog.CreatedCount == 1 ? string.Empty : "s")}.";
            RefreshOverview();
        }
    }

    private void QueueOpenCreateLayouts(Guid? preferredFolderId)
    {
        // Native macOS menus finish dismissing after their click callback. Delay
        // Rhino display-mode and block enumeration until that tracking loop exits.
        Application.Instance.AsyncInvoke(() => OpenCreateLayouts(preferredFolderId));
    }

    private void ApplyResponsiveLayout()
    {
        if (_isApplyingResponsiveLayout)
        {
            return;
        }

        _isApplyingResponsiveLayout = true;
        try
        {
            var next = FoundryResponsiveLayout.Transition(
                Math.Max(Width, 1),
                _responsiveLayout.Density);
            if (next == _responsiveLayout)
            {
                return;
            }

            var dimensionsChanged = next.ThumbnailWidth != _responsiveLayout.ThumbnailWidth ||
                                    next.ThumbnailHeight != _responsiveLayout.ThumbnailHeight;
            _responsiveLayout = next;
            // Eto maps this to NSTableColumn.setHidden on macOS. Toggling that
            // property while Rhino's dock splitter runs its nested tracking loop
            // recursively enters AppKit geometry validation. Keep the column on
            // Mac; compact mode still stacks tools, reduces previews, and hides
            // secondary footer copy. Windows can safely collapse the column.
            if (!OperatingSystem.IsMacOS())
            {
                _detailsColumn.Visible = next.ShowSecondaryColumn;
            }
            _toolbarSurface.Content = CreateToolbarContent();
            _folderDraftStrip.Content = CreateFolderDraftContent();
            if (dimensionsChanged && _overview.DocumentRuntimeSerialNumber is { } serial)
            {
                InvalidateThumbnails(serial, null);
                PopulateTree();
            }
        }
        finally
        {
            _isApplyingResponsiveLayout = false;
        }
    }

    private void QueueResponsiveLayout()
    {
        if (!_isLoaded)
        {
            return;
        }

        // Coalesce Windows resize bursts so toolbar reflow and preview-size
        // invalidation happen once after the splitter settles. Mac live density
        // changes are disabled at subscription time because of AppKit recursion.
        _responsiveTimer.Stop();
        _responsiveTimer.Start();
    }

    private OverviewThumbnailKey ThumbnailKey(Guid sheetPageViewId)
    {
        return new OverviewThumbnailKey(
            _overview.DocumentRuntimeSerialNumber ?? 0,
            sheetPageViewId,
            _responsiveLayout.ThumbnailWidth,
            _responsiveLayout.ThumbnailHeight);
    }

    private void QueueThumbnails()
    {
        if (_usesMacSafeHierarchy ||
            !_isLoaded ||
            _overview.DocumentRuntimeSerialNumber is null)
        {
            return;
        }

        var selectedSheetId = SelectedItems()
            .FirstOrDefault(item => item.Node.Key.Kind == OverviewNodeKind.Sheet)?
            .Node.Key.Id;
        var index = 0;
        foreach (var pair in _sheetItems)
        {
            var key = ThumbnailKey(pair.Key);
            if (_thumbnailCache.TryGet(key, out _) || _thumbnailBitmaps.ContainsKey(key))
            {
                continue;
            }

            var priority = pair.Key == selectedSheetId
                ? -1
                : index < 18
                    ? index
                    : 100 + index;
            _thumbnailQueue.Enqueue(new OverviewThumbnailRequest(key, priority));
            index++;
        }

        if (_thumbnailQueue.PendingCount > 0 && !_thumbnailTimer.Started)
        {
            _thumbnailTimer.Start();
        }
    }

    private async Task CaptureNextThumbnailAsync()
    {
        if (_thumbnailCaptureInProgress)
        {
            return;
        }

        var request = _thumbnailQueue.TakeNext();
        if (request is null)
        {
            _thumbnailTimer.Stop();
            return;
        }

        _thumbnailCaptureInProgress = true;
        try
        {
            var result = await LayoutFoundryUiHost.CaptureThumbnailAsync(
                request,
                _thumbnailCancellation.Token);
            if (!result.Succeeded ||
                result.Key.DocumentRuntimeSerialNumber != _overview.DocumentRuntimeSerialNumber)
            {
                return;
            }

            _thumbnailCache.Store(result.Key, result.PngBytes!);
            if (_thumbnailBitmaps.Remove(result.Key, out var previous))
            {
                previous.Dispose();
            }

            var bitmap = new Bitmap(result.PngBytes!);
            _thumbnailBitmaps[result.Key] = bitmap;
            if (_sheetItems.TryGetValue(result.Key.SheetPageViewId, out var item))
            {
                item.Thumbnail = bitmap;
                _treeGrid.ReloadItem(item, reloadChildren: false);
            }

            TrimBitmapCache();
        }
        finally
        {
            _thumbnailQueue.Complete(request.Key);
            _thumbnailCaptureInProgress = false;
        }
    }

    private void InvalidateThumbnails(uint documentSerial, IReadOnlySet<Guid>? sheetIds)
    {
        _thumbnailCache.Invalidate(documentSerial, sheetIds);
        foreach (var pair in _thumbnailBitmaps
                     .Where(pair =>
                         pair.Key.DocumentRuntimeSerialNumber == documentSerial &&
                         (sheetIds is null || sheetIds.Count == 0 || sheetIds.Contains(pair.Key.SheetPageViewId)))
                     .ToArray())
        {
            _thumbnailBitmaps.Remove(pair.Key);
            pair.Value.Dispose();
        }
    }

    private void TrimBitmapCache()
    {
        foreach (var pair in _thumbnailBitmaps.ToArray())
        {
            if (_thumbnailCache.TryGet(pair.Key, out _))
            {
                continue;
            }

            _thumbnailBitmaps.Remove(pair.Key);
            pair.Value.Dispose();
        }
    }

    private void ResetThumbnailCapture()
    {
        _thumbnailTimer.Stop();
        _thumbnailCancellation.Cancel();
        _thumbnailCancellation.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        _thumbnailQueue.Clear();
        foreach (var bitmap in _thumbnailBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _thumbnailBitmaps.Clear();
        _thumbnailCaptureInProgress = false;
    }

    private void ClearSelection()
    {
        _selection.Clear();
        LayoutFoundryUiHost.Selection.Clear(_overview.DocumentRuntimeSerialNumber, this);
        _isPopulatingTree = true;
        try
        {
            _treeGrid.SelectedItem = null;
        }
        finally
        {
            _isPopulatingTree = false;
        }

        UpdatePresentation();
    }

    private int SelectedItemCount()
    {
        return _treeGrid.SelectedItems.Cast<object>().Count();
    }

    private IReadOnlyList<HierarchyTreeItem> SelectedItems()
    {
        return _treeGrid.SelectedItems.OfType<HierarchyTreeItem>().ToArray();
    }

    private OverviewNodeKey[] SelectedKeys() => _selection.Selected.Distinct().ToArray();

    private bool IsDocumentRootKey(OverviewNodeKey key) =>
        key.Kind == OverviewNodeKind.Folder && key.Id == _overview.RootFolderId;

    private IEnumerable<SheetOverview> SelectedSheets()
    {
        return SelectedItems()
            .Select(item => item.Node.Sheet)
            .Where(sheet => sheet is not null)
            .Cast<SheetOverview>();
    }

    private static IEnumerable<OverviewTreeNode> Flatten(IEnumerable<OverviewTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<HierarchyTreeItem> Flatten(IEnumerable<HierarchyTreeItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children.OfType<HierarchyTreeItem>()))
            {
                yield return child;
            }
        }
    }

    partial void RestoreVisibleTreeSelection(IReadOnlyList<HierarchyTreeItem> visibleRows);

    private static string PaperLabel(SheetOverview sheet)
    {
        var preset = PaperSizeChoices.FirstOrDefault(choice =>
            string.Equals(choice.UnitSystem, sheet.PageUnitSystem, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(choice.Width - sheet.PageWidth) < 0.01 &&
            Math.Abs(choice.Height - sheet.PageHeight) < 0.01);
        return preset?.Label ??
               $"{sheet.PageWidth:0.###} × {sheet.PageHeight:0.###} {UnitAbbreviation(sheet.PageUnitSystem)}";
    }

    private static string UnitAbbreviation(string unitSystem) => unitSystem switch
    {
        "Millimeters" => "mm",
        "Centimeters" => "cm",
        "Meters" => "m",
        "Inches" => "in",
        "Feet" => "ft",
        _ => unitSystem,
    };

    private static readonly PaperSizeChoice[] PaperSizeChoices =
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

    private sealed class HierarchyTreeItem : TreeGridItem
    {
        public HierarchyTreeItem(
            OverviewTreeNode node,
            bool expandAll,
            OverviewNodeKey preferredSelection,
            bool useMacSafeSingleColumn,
            Guid? inlineDraftId,
            HierarchyExpansionState expansionState)
        {
            Node = node;
            Presentation = OverviewRowPresentation.Create(node, useMacSafeSingleColumn: false);
            IsInlineDraft = inlineDraftId == node.Key.Id;
            RowIcon = node.IsDocumentRoot
                ? FoundryHierarchyIcons.Rhino
                : node.Key.Kind switch
                {
                    OverviewNodeKind.Folder => FoundryHierarchyIcons.Folder,
                    OverviewNodeKind.Sheet => FoundryHierarchyIcons.Layout,
                    OverviewNodeKind.Detail => FoundryHierarchyIcons.Detail,
                    OverviewNodeKind.LayerState => FoundryHierarchyIcons.Detail,
                    OverviewNodeKind.ObjectDisplayState => FoundryHierarchyIcons.Detail,
                    _ => null,
                };
            _displayText = IsInlineDraft
                ? node.Label
                : RowIcon is not null
                    ? node.Label
                    : Presentation.PrimaryText;
            foreach (var child in node.Children)
            {
                Children.Add(new HierarchyTreeItem(
                    child,
                    expandAll,
                    preferredSelection,
                    useMacSafeSingleColumn,
                    inlineDraftId,
                    expansionState));
            }

            Expanded = expansionState.Resolve(
                node.Key,
                expandedByDefault: node.Key.Kind == OverviewNodeKind.Folder,
                forceExpanded: expandAll,
                containsPreferredSelection: Contains(node.Children, preferredSelection));
        }

        public OverviewTreeNode Node { get; }

        public OverviewRowPresentation Presentation { get; }

        public bool IsInlineDraft { get; private set; }

        public Image? RowIcon { get; }

        private string _displayText;

        public string DisplayText
        {
            get => _displayText;
            set
            {
                if (IsInlineDraft)
                {
                    _displayText = value;
                }
            }
        }

        public void BeginInlineEdit()
        {
            IsInlineDraft = true;
        }

        public void CancelInlineEdit()
        {
            _displayText = Node.Label;
            IsInlineDraft = false;
        }

        public string PrimaryText => Presentation.PrimaryText;

        public string SecondaryText => Presentation.SecondaryText;

        public string StatusText => Presentation.StatusText;

        public FoundryBadgeTone StatusTone => Node.Issues.Count == 0
            ? FoundryBadgeTone.Neutral
            : Node.Issues.Max(issue => issue.Severity) switch
            {
                OverviewIssueSeverity.Error => FoundryBadgeTone.Error,
                OverviewIssueSeverity.Warning => FoundryBadgeTone.Warning,
                _ => FoundryBadgeTone.Neutral,
            };

        public bool HasSheetTargets => DescendantSheets(Node).Any();

        public bool HasDetailTargets => DescendantDetails(Node).Any();

        public bool AllPrintIncluded
        {
            get
            {
                var sheets = DescendantSheets(Node).ToArray();
                return sheets.Length > 0 && sheets.All(sheet => sheet.IncludeInPrintAll);
            }
        }

        public string PrintText
        {
            get
            {
                if (Node.Key.Kind == OverviewNodeKind.Detail) return string.Empty;
                var sheets = DescendantSheets(Node).ToArray();
                if (sheets.Length == 0) return string.Empty;
                return sheets.All(sheet => sheet.IncludeInPrintAll)
                    ? "●"
                    : sheets.All(sheet => !sheet.IncludeInPrintAll)
                        ? "○"
                        : "◐";
            }
        }

        public string TemplateText => CapabilitySummary(Node.Folder?.TemplateCapabilities ??
                                                        Node.Sheet?.TemplateCapabilities ??
                                                        Node.Detail?.TemplateCapabilities ??
                                                        TemplateCapability.None);

        public string LayerStateText => Node.AppearanceState is { Kind: AppearanceStateKind.LayerState } state
            ? $"{state.RuleCount} rule{(state.RuleCount == 1 ? string.Empty : "s")} · {state.DirectAssignmentCount} use{(state.DirectAssignmentCount == 1 ? string.Empty : "s")}"
            : StateBinding(AppearanceStateKind.LayerState) is { } binding
                ? $"{binding.Name}{(binding.IsInherited ? " · inherited" : string.Empty)}"
            : AppearanceSummary() is not { } appearance
            ? "—"
            : appearance.IsMixed
                ? "Mixed"
                : appearance.VisibleLayerCount == 0 && appearance.HiddenLayerCount == 0
                    ? "Inherited"
                    : $"On {appearance.VisibleLayerCount} · Off {appearance.HiddenLayerCount}";

        public string ObjectModesText => Node.AppearanceState is { Kind: AppearanceStateKind.ObjectDisplayState } state
            ? $"{state.RuleCount} rule{(state.RuleCount == 1 ? string.Empty : "s")} · {state.DirectAssignmentCount} use{(state.DirectAssignmentCount == 1 ? string.Empty : "s")}"
            : StateBinding(AppearanceStateKind.ObjectDisplayState) is { } binding
                ? $"{binding.Name}{(binding.IsInherited ? " · inherited" : string.Empty)}"
            : AppearanceSummary() is not { } appearance
            ? "—"
            : appearance.IsMixed
                ? "Mixed"
                : appearance.ObjectDisplayOverrideCount == 0
                    ? "Inherited"
                    : appearance.UnresolvedCount > 0
                        ? $"{appearance.ObjectDisplayOverrideCount} · {appearance.UnresolvedCount} missing"
                        : $"{appearance.ObjectDisplayOverrideCount} override{(appearance.ObjectDisplayOverrideCount == 1 ? string.Empty : "s")}";

        private ViewportAppearanceSummary? AppearanceSummary() =>
            Node.Folder?.Appearance ?? Node.Sheet?.Appearance ?? Node.Detail?.Appearance;

        private AppearanceStateBindingOverview? StateBinding(AppearanceStateKind kind) => kind switch
        {
            AppearanceStateKind.LayerState =>
                Node.Folder?.LayerState ?? Node.Sheet?.LayerState ?? Node.Detail?.LayerState,
            AppearanceStateKind.ObjectDisplayState =>
                Node.Folder?.ObjectDisplayState ?? Node.Sheet?.ObjectDisplayState ?? Node.Detail?.ObjectDisplayState,
            _ => null,
        };

        private static string CapabilitySummary(TemplateCapability capabilities)
        {
            if (capabilities == TemplateCapability.None) return "—";
            var labels = new List<string>(2);
            if (capabilities.HasFlag(TemplateCapability.Layout)) labels.Add("Layout");
            if (capabilities.HasFlag(TemplateCapability.TitleBlock)) labels.Add("Title block");
            return labels.Count == 0 ? "—" : string.Join(" · ", labels);
        }

        private string? _paperText;

        public string PaperText
        {
            get => _paperText ??= PropertySummary(
                DescendantSheets(Node).Select(PaperLabel),
                Node.Key.Kind == OverviewNodeKind.Detail ? string.Empty : "—");
            set => _paperText = value;
        }

        public string PaperCellText => PaperText is "" or "—" ? PaperText : $"{PaperText}  ▾";

        public string DetailsText => Node.Key.Kind switch
        {
            OverviewNodeKind.Detail => string.Empty,
            OverviewNodeKind.Sheet => Node.Sheet?.DetailCount.ToString() ?? "0",
            OverviewNodeKind.Folder => DescendantDetails(Node).Count().ToString(),
            _ => string.Empty,
        };

        private string? _displayModeText;

        public string DisplayModeText
        {
            get => _displayModeText ??= PropertySummary(
                DescendantDetails(Node).Select(detail => detail.DisplayModeName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                "—");
            set => _displayModeText = value;
        }

        public string DisplayModeCellText => DisplayModeText is "" or "—"
            ? DisplayModeText
            : $"{DisplayModeText}  ▾";

        public Image? Thumbnail { get; set; }

        private static string PropertySummary(IEnumerable<string> values, string empty)
        {
            var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            return distinct.Length switch
            {
                0 => empty,
                1 => distinct[0],
                _ => "Mixed",
            };
        }

        private static IEnumerable<SheetOverview> DescendantSheets(OverviewTreeNode node)
        {
            if (node.Sheet is not null) yield return node.Sheet;
            foreach (var child in node.Children)
            foreach (var sheet in DescendantSheets(child))
                yield return sheet;
        }

        private static IEnumerable<DetailOverview> DescendantDetails(OverviewTreeNode node)
        {
            if (node.Sheet is not null)
            {
                foreach (var detail in node.Sheet.Details)
                    yield return detail;
                yield break;
            }
            if (node.Detail is not null)
            {
                yield return node.Detail;
                yield break;
            }
            foreach (var child in node.Children)
            foreach (var detail in DescendantDetails(child))
                yield return detail;
        }

        private static bool Contains(
            IEnumerable<OverviewTreeNode> nodes,
            OverviewNodeKey key)
        {
            return nodes.Any(node => node.Key == key || Contains(node.Children, key));
        }
    }

    private enum InlineDraftKind
    {
        Folder,
        Sheet,
        LayerState,
        ObjectDisplayState,
        RenameFolder,
        RenameSheet,
        RenameLayerState,
        RenameObjectDisplayState,
    }

    private enum FoundryPanelViewMode
    {
        List,
        Thumbnail,
        Canvas,
    }

    private sealed record CellInteractionGuard(OverviewNodeKey Key, GridColumn Column);

    private sealed record PendingDeleteSelection(
        uint DocumentRuntimeSerialNumber,
        long SourceRevision,
        IReadOnlyList<OverviewNodeKey> Selection,
        string Summary);

    private sealed class InlineDraft(
        InlineDraftKind kind,
        Guid id,
        Guid parentFolderId,
        string name)
    {
        public InlineDraftKind Kind { get; } = kind;

        public Guid Id { get; } = id;

        public Guid ParentFolderId { get; } = parentFolderId;

        public string OriginalName { get; } = name;

        public string Name { get; set; } = name;
    }
}
