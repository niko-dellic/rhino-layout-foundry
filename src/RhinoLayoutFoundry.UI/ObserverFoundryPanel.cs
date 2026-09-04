using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

[Guid("b9bbcc68-9598-4899-96b8-7a79211693b5")]
public sealed class ObserverFoundryPanel : Panel
{
    private readonly ObserverCanvasDrawable _canvas;
    private readonly GridView _navigator;
    private readonly Label _status;
    private readonly FoundryToolbarIconButton _navigatorButton;
    private readonly FoundryToolbarIconButton _namedViewsButton;
    private readonly SelectionInspectorPanel _inspector;
    private readonly FoundryToolbarIconButton _nestedPackingButton;
    private readonly FoundryToolbarIconButton _compactPackingButton;
    private readonly FoundryToolbarIconButton _appearanceCardsButton;
    private readonly FoundryToolbarIconButton _appearanceConnectionsButton;
    private readonly FoundryToolbarIconButton _appearanceBadgesButton;
    private readonly FoundryToolbarButtonGroup _appearancePresentationGroup;
    private readonly FoundryToolbarIconButton _gridAppearanceButton;
    private readonly CanvasGridTray _gridAppearanceTray;
    private readonly PixelLayout _canvasOverlay;
    private readonly Control _canvasToolbar;
    private readonly UITimer _overlayLayoutTimer;
    private readonly UITimer _thumbnailTimer;
    private readonly UITimer _invalidationTimer;
    private readonly OverviewThumbnailCache _thumbnailCache = new(128, 64 * 1024 * 1024);
    private readonly OverviewThumbnailRequestQueue _thumbnailQueue = new();
    private readonly Queue<NamedViewThumbnailRequest> _namedViewThumbnailQueue = new();
    private readonly HashSet<NamedViewThumbnailKey> _pendingNamedViewThumbnails = [];
    private readonly Dictionary<Guid, long> _previewContentVersions = [];
    private readonly Dictionary<uint, ObserverCamera> _documentCameras = [];
    private readonly object _invalidationSyncRoot = new();
    private ObserverSnapshot _snapshot = ObserverSnapshot.NoDocument;
    private OverviewFilterProjection _filter = new(false, new HashSet<OverviewNodeKey>(), new HashSet<Guid>());
    private NavigatorRow[] _navigatorRows = [];
    private OverviewInvalidation? _pendingInvalidation;
    private CancellationTokenSource _thumbnailCancellation = new();
    private bool _thumbnailCaptureInProgress;
    private bool _isLoaded;
    private bool _updatingNavigatorSelection;
    private uint? _pendingInitialFitDocumentSerial;
    private long _previewContentSequence;
    private long _namedViewPreviewContentVersion;

    private const int MinimumInitialFitViewportDimension = 96;

    internal event EventHandler? ExitFullscreenRequested;

    internal event EventHandler<DeleteSelectionRequestedEventArgs>? DeleteSelectionRequested;

    internal void FocusContent() => _canvas.Focus();

    internal Control AppearancePresentationControl => _appearancePresentationGroup;

    public ObserverFoundryPanel()
    {
        BackgroundColor = FoundryTheme.PanelBackground;
        _canvas = new ObserverCanvasDrawable();
        _navigator = CreateNavigator();
        _status = FoundryTheme.MutedLabel();
        _status.Visible = false;
        _status.TextChanged += (_, _) =>
            _status.Visible = !string.IsNullOrWhiteSpace(_status.Text);

        var fitButton = ToolbarButton(FoundryViewIcons.FitAll(), "Fit all layouts in the canvas");
        var focusButton = ToolbarButton(FoundryViewIcons.FocusSelection(), "Focus the current selection");
        var tidyButton = ToolbarButton(FoundryViewIcons.Tidy(), "Tidy the selected layouts or folders, or the whole board");
        _gridAppearanceButton = ToolbarToggleButton(
            FoundryViewIcons.GridAppearance(),
            "Adjust the canvas grid color and opacity");
        var zoomOutButton = ToolbarButton(FoundryViewIcons.ZoomOut(), "Zoom out");
        var zoomInButton = ToolbarButton(FoundryViewIcons.ZoomIn(), "Zoom in");
        _navigatorButton = ToolbarToggleButton(FoundryViewIcons.Navigator(), "Show or hide the Navigator");
        _navigatorButton.Checked = true;
        _namedViewsButton = ToolbarToggleButton(FoundryViewIcons.Properties(), "Show or hide the selection Inspector");
        _nestedPackingButton = ToolbarToggleButton(
            FoundryViewIcons.NestedPacking(),
            "Nest child folder containers inside their parent folders");
        _nestedPackingButton.Checked = true;
        _compactPackingButton = ToolbarToggleButton(
            FoundryViewIcons.CompactPacking(),
            "Hide folder containers and tightly pack every layout");
        _appearanceCardsButton = ToolbarToggleButton(
            FoundryViewIcons.AppearanceCards(),
            "Show appearance states as standalone cards");
        _appearanceCardsButton.Checked = true;
        _appearanceConnectionsButton = ToolbarToggleButton(
            FoundryViewIcons.AppearanceConnections(),
            "Show appearance-state cards with assignment connections");
        _appearanceBadgesButton = ToolbarToggleButton(
            FoundryViewIcons.AppearanceBadges(),
            "Show direct appearance-state assignments as target badges");
        _appearancePresentationGroup = new FoundryToolbarButtonGroup(
            _appearanceCardsButton,
            _appearanceConnectionsButton,
            _appearanceBadgesButton);
        var openButton = ToolbarButton(FoundryViewIcons.OpenSelection(), "Open the selected layout or detail in Rhino");
        fitButton.Click += (_, _) => _canvas.FitAll();
        focusButton.Click += (_, _) => _canvas.FocusSelection();
        tidyButton.Click += async (_, _) => await TidyAsync();
        _gridAppearanceButton.Click += (_, _) => ApplyGridAppearanceTrayVisibility();
        zoomOutButton.Click += (_, _) => _canvas.Zoom(1 / 1.2);
        zoomInButton.Click += (_, _) => _canvas.Zoom(1.2);
        _navigatorButton.Click += (_, _) => ApplySidebarVisibility();
        _namedViewsButton.Click += (_, _) => ApplySidebarVisibility();
        _nestedPackingButton.Click += (_, _) => SetPackingMode(ObserverPackingMode.NestedFolders);
        _compactPackingButton.Click += (_, _) => SetPackingMode(ObserverPackingMode.CompactSheets);
        _appearanceCardsButton.Click += (_, _) =>
            SetAppearancePresentationMode(ObserverAppearancePresentationMode.Cards);
        _appearanceConnectionsButton.Click += (_, _) =>
            SetAppearancePresentationMode(ObserverAppearancePresentationMode.CardsWithConnections);
        _appearanceBadgesButton.Click += (_, _) =>
            SetAppearancePresentationMode(ObserverAppearancePresentationMode.AssignmentBadges);
        openButton.Click += (_, _) => NavigateSelection();

        _canvasToolbar = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = FoundryTheme.Space1,
            VerticalContentAlignment = VerticalAlignment.Center,
            Items =
            {
                _navigatorButton,
                ToolbarSeparator(),
                fitButton,
                focusButton,
                tidyButton,
                ToolbarSeparator(),
                _nestedPackingButton,
                _compactPackingButton,
                ToolbarSeparator(),
                _gridAppearanceButton,
                ToolbarSeparator(),
                zoomOutButton,
                zoomInButton,
                new StackLayoutItem(null, true),
                _namedViewsButton,
                openButton,
            },
        };

        _canvasOverlay = new PixelLayout
        {
            BackgroundColor = FoundryTheme.CanvasBackground,
        };
        _gridAppearanceTray = new CanvasGridTray(
            _canvas.GridColor,
            _canvas.GridOpacity,
            (color, opacity) => _canvas.SetGridAppearance(color, opacity))
        {
            Visible = false,
        };
        _canvas.MouseDown += (_, _) => DismissAppearanceTrays();
        _overlayLayoutTimer = new UITimer { Interval = 0.04 };
        _overlayLayoutTimer.Elapsed += (_, _) =>
        {
            _overlayLayoutTimer.Stop();
            UpdateCanvasOverlayLayout();
        };
        _canvasOverlay.Add(_canvas, 0, 0);
        _canvasOverlay.Add(_canvasToolbar, 0, 0);
        _canvasOverlay.Add(_gridAppearanceTray, 0, 36);
        _inspector = new SelectionInspectorPanel { Visible = false };
        _canvasOverlay.Add(_inspector, 0, 38);
        _inspector.OperationCompleted += (_, eventArgs) =>
        {
            _status.Text = ResultMessage(eventArgs.Result, eventArgs.SuccessMessage);
            if (eventArgs.Result.Succeeded) RefreshSnapshot(fit: false);
        };
        _inspector.NamedViewPreviewsRequested += (_, _) => QueueNamedViewPreviews();
        _canvasOverlay.SizeChanged += (_, _) => QueueCanvasOverlayLayout();
        ApplySidebarVisibility();
        Content = new StackLayout
        {
            Padding = new Padding(0),
            Spacing = FoundryTheme.Space2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new StackLayoutItem(_canvasOverlay, true),
                _status,
            },
        };

        _canvas.ViewChanged += (_, _) =>
        {
            RememberCurrentCamera(_snapshot.DocumentRuntimeSerialNumber);
            QueueVisiblePreviews();
        };
        _canvas.SelectionRequested += (_, eventArgs) =>
            LayoutFoundryUiHost.Selection.Replace(
                _snapshot.HasDocument ? _snapshot.DocumentRuntimeSerialNumber : null,
                eventArgs.Selection,
                eventArgs.Anchor,
                this);
        _canvas.NavigationRequested += (_, eventArgs) => Navigate(eventArgs.Target);
        _canvas.BoardStateRequested += async (_, eventArgs) =>
            await ApplyBoardStateAsync(eventArgs.State, eventArgs.UndoDescription);
        _canvas.HierarchyMoveRequested += async (_, eventArgs) =>
            await MoveHierarchyAsync(eventArgs);
        _canvas.HierarchyPlacementRequested += async (_, eventArgs) =>
            await ReorganizeHierarchyAsync(eventArgs);
        _canvas.ReorderStepRequested += async (_, eventArgs) =>
            await ReorderSelectionByStepAsync(eventArgs.Direction);
        _canvas.NamedViewRequested += async (_, eventArgs) =>
            await AssignNamedViewAsync(eventArgs);
        _canvas.AssignNamedViewToSelectionRequested += async (_, eventArgs) =>
            await AssignSelectedNamedViewAsync(eventArgs.NamedViewName);
        _canvas.NamedViewPreviewsRequested += (_, _) => QueueNamedViewPreviews();
        _canvas.ContextRequested += (_, eventArgs) => ShowContextMenu(
            eventArgs.ControlPoint,
            eventArgs.DestinationFolderId,
            new ObserverPointRecord(eventArgs.WorldPoint.X, eventArgs.WorldPoint.Y));
        _canvas.DeleteRequested += (_, _) => RequestDeleteSelection();
        _canvas.TidyRequested += async (_, _) => await TidyAsync();
        _canvas.ExitWorkspaceRequested += (_, _) => ExitFullscreenRequested?.Invoke(this, EventArgs.Empty);
        _canvas.FolderDraftRequested += async (_, eventArgs) =>
            await CommitNavigatorFolderDraftAsync(eventArgs);
        _canvas.CopyRequested += (_, _) => CopySelection();
        _canvas.PasteRequested += async (_, eventArgs) =>
            await PasteSelectionAsync(eventArgs.DestinationFolderId, eventArgs.TargetOrigin);

        _navigator.SelectedRowsChanged += OnNavigatorSelectionChanged;
        _navigator.CellFormatting += (_, eventArgs) =>
        {
            if (eventArgs.Item is not NavigatorRow row ||
                !_filter.IsActive ||
                _filter.Emphasizes(row.Key) ||
                LayoutFoundryUiHost.Selection.Selected.Contains(row.Key))
                return;

            eventArgs.ForegroundColor = FoundryTheme.WithAlpha(FoundryTheme.MutedText, 72);
        };
        _navigator.CellDoubleClick += (_, _) => _canvas.FocusSelection();
        _navigator.KeyDown += (_, eventArgs) =>
        {
            if (HierarchyClipboard.IsCopyShortcut(eventArgs))
            {
                CopySelection();
                eventArgs.Handled = true;
                return;
            }
            if (HierarchyClipboard.IsPasteShortcut(eventArgs))
            {
                _ = PasteSelectionAsync();
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key != Keys.F)
                return;

            _canvas.FocusSelection();
            eventArgs.Handled = true;
        };
        _thumbnailTimer = new UITimer { Interval = 0.06 };
        _thumbnailTimer.Elapsed += async (_, _) => await CaptureNextThumbnailAsync();
        _invalidationTimer = new UITimer { Interval = 0.12 };
        _invalidationTimer.Elapsed += OnInvalidationTimer;
        Load += OnLoaded;
        UnLoad += OnUnloaded;
        // The panel has no meaningful viewport during construction. Fitting here would
        // clamp the camera to its minimum zoom and incorrectly remember that provisional
        // view. OnLoaded defers the first fit until the canvas has real dimensions.
        RefreshSnapshot(fit: false);
    }

    internal void SetFullscreenState(bool fullscreen)
    {
        ApplySidebarVisibility();
        _canvas.ExitWorkspaceOnEscape = fullscreen;
        _canvas.Invalidate();
    }

    internal void BeginInlineFolderCreation(Guid? preferredParentFolderId)
    {
        if (!_snapshot.HasDocument) return;
        var parentFolderId = preferredParentFolderId is { } preferred &&
                             _snapshot.Folders.Any(folder => folder.Id == preferred)
            ? preferred
            : _snapshot.RootFolderId;
        _navigatorButton.Checked = true;
        ApplySidebarVisibility();
        _canvas.BeginNavigatorFolderDraft(parentFolderId);
        _status.Text = string.Empty;
    }

    private async Task CommitNavigatorFolderDraftAsync(ObserverFolderDraftRequestedEventArgs eventArgs)
    {
        _status.Text = "Creating folder…";
        var result = await LayoutFoundryUiHost.CreateFolderAsync(
            eventArgs.FolderId,
            eventArgs.ParentFolderId,
            eventArgs.Name);
        if (!result.Succeeded)
        {
            _canvas.ResumeNavigatorFolderDraft();
            _status.Text = ResultMessage(result, string.Empty);
            return;
        }

        _canvas.CancelNavigatorFolderDraft();
        var key = new OverviewNodeKey(OverviewNodeKind.Folder, eventArgs.FolderId);
        LayoutFoundryUiHost.Selection.Replace(
            _snapshot.DocumentRuntimeSerialNumber,
            [key],
            key,
            this);
        _status.Text = $"Created folder '{eventArgs.Name}'.";
        RefreshSnapshot(fit: false);
    }

    internal void SetFilter(OverviewFilterProjection projection)
    {
        _filter = projection ?? throw new ArgumentNullException(nameof(projection));
        _canvas.SetFilter(_filter);
        _navigator.ReloadData(Enumerable.Range(0, _navigatorRows.Length));
        QueueVisiblePreviews();
    }

    private void ApplySidebarVisibility()
    {
        _canvas.SetNavigatorVisible(_navigatorButton.Checked);
        _canvas.SetNamedViewsVisible(false);
        _inspector.Visible = _namedViewsButton.Checked;
        UpdateCanvasOverlayLayout();
    }

    private void SetPackingMode(ObserverPackingMode packingMode)
    {
        if (packingMode == ObserverPackingMode.CompactSheets &&
            _canvas.AppearancePresentationMode == ObserverAppearancePresentationMode.AssignmentBadges)
            return;
        _nestedPackingButton.Checked = packingMode == ObserverPackingMode.NestedFolders;
        _compactPackingButton.Checked = packingMode == ObserverPackingMode.CompactSheets;
        _canvas.SetPackingMode(packingMode, fit: true);
        _status.Text = packingMode == ObserverPackingMode.NestedFolders
            ? "Nested packing: child folders are contained inside their parent folders."
            : "Compact packing: layouts are tightly arranged without folder containers.";
        QueueVisiblePreviews();
    }

    private void SetAppearancePresentationMode(ObserverAppearancePresentationMode mode)
    {
        _appearanceCardsButton.Checked = mode == ObserverAppearancePresentationMode.Cards;
        _appearanceConnectionsButton.Checked = mode == ObserverAppearancePresentationMode.CardsWithConnections;
        _appearanceBadgesButton.Checked = mode == ObserverAppearancePresentationMode.AssignmentBadges;
        if (mode == ObserverAppearancePresentationMode.AssignmentBadges)
        {
            _nestedPackingButton.Checked = true;
            _compactPackingButton.Checked = false;
            _compactPackingButton.Enabled = false;
            _canvas.SetPackingMode(ObserverPackingMode.NestedFolders, fit: false);
        }
        else
        {
            _compactPackingButton.Enabled = true;
        }
        _canvas.SetAppearancePresentationMode(mode);
        _status.Text = mode switch
        {
            ObserverAppearancePresentationMode.Cards => "Appearance states are shown as standalone cards.",
            ObserverAppearancePresentationMode.CardsWithConnections =>
                "Appearance states are connected to their direct assignment targets.",
            _ => "Direct appearance-state assignments are shown as badges on their targets.",
        };
    }

    private void ApplyGridAppearanceTrayVisibility()
    {
        _gridAppearanceTray.Visible = _gridAppearanceButton.Checked;
        UpdateCanvasOverlayLayout();
    }

    private void DismissAppearanceTrays()
    {
        if (!_gridAppearanceTray.Visible) return;
        _gridAppearanceButton.Checked = false;
        _gridAppearanceTray.Visible = false;
        UpdateCanvasOverlayLayout();
    }

    private void UpdateCanvasOverlayLayout()
    {
        var clientSize = _canvasOverlay.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
            return;

        _canvas.Size = clientSize;
        _canvasToolbar.Size = new Size(clientSize.Width, 28);
        _canvasOverlay.Move(_canvasToolbar, 0, 0);
        _inspector.Size = new Size(
            Math.Min(SelectionInspectorPanel.OverlayWidth, Math.Max(0, clientSize.Width)),
            Math.Max(0, clientSize.Height - 38));
        _canvasOverlay.Move(
            _inspector,
            Math.Max(0, clientSize.Width - _inspector.Width),
            38);
        var trayX = Math.Clamp(
            _gridAppearanceButton.Location.X,
            0,
            Math.Max(0, clientSize.Width - _gridAppearanceTray.Width));
        _canvasOverlay.Move(_gridAppearanceTray, trayX, 36);
        TryApplyPendingInitialFit(clientSize);
        QueueNamedViewPreviews();
    }

    private void QueueCanvasOverlayLayout()
    {
        _overlayLayoutTimer.Stop();
        _overlayLayoutTimer.Start();
    }

    private static FoundryToolbarIconButton ToolbarButton(Image image, string toolTip) =>
        new(image, toolTip);

    private static FoundryToolbarIconButton ToolbarToggleButton(Image image, string toolTip) =>
        new(image, toolTip, isToggle: true);

    private static Control ToolbarSeparator() => new Panel
    {
        Width = 1,
        Height = 18,
        BackgroundColor = FoundryTheme.CanvasBorder,
    };

    private static GridView CreateNavigator()
    {
        var grid = new GridView
        {
            AllowMultipleSelection = true,
            AllowEmptySelection = true,
            ShowHeader = false,
            Border = BorderType.None,
            BackgroundColor = Colors.Transparent,
            ToolTip = "Select folders, layouts, or details; double-click or press F to frame them on the Canvas",
        };
        grid.Columns.Add(new GridColumn
        {
            DataCell = new TextBoxCell
            {
                Binding = Binding.Property<NavigatorRow, string>(row => row.Label),
            },
            AutoSize = true,
        });
        return grid;
    }

    private void OnLoaded(object? sender, EventArgs eventArgs)
    {
        if (_isLoaded) return;
        _isLoaded = true;
        LayoutFoundryUiHost.OverviewChanged += OnOverviewChanged;
        LayoutFoundryUiHost.Selection.Changed += OnSharedSelectionChanged;
        QueueCanvasOverlayLayout();
        RefreshSnapshot(fit: true);
    }

    private void OnUnloaded(object? sender, EventArgs eventArgs)
    {
        if (!_isLoaded) return;
        _isLoaded = false;
        LayoutFoundryUiHost.OverviewChanged -= OnOverviewChanged;
        LayoutFoundryUiHost.Selection.Changed -= OnSharedSelectionChanged;
        _overlayLayoutTimer.Stop();
        _invalidationTimer.Stop();
        ResetThumbnailCapture();
        _inspector.ClearNamedViewPreviews();
        _canvas.ReleasePreviews();
    }

    private void OnOverviewChanged(object? sender, OverviewInvalidationEventArgs eventArgs)
    {
        lock (_invalidationSyncRoot)
        {
            _pendingInvalidation = _pendingInvalidation is null
                ? eventArgs.Invalidation
                : _pendingInvalidation.Merge(eventArgs.Invalidation);
        }

        if (!_invalidationTimer.Started) _invalidationTimer.Start();
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

        if (invalidation is null) return;
        if (invalidation.Kind.HasFlag(OverviewInvalidationKind.Thumbnails))
        {
            var affectedSheetIds = ResolveAffectedSheetIds(invalidation.AffectedEntityIds);
            AdvancePreviewVersions(affectedSheetIds.Count == 0 ? null : affectedSheetIds);
            _canvas.InvalidatePreviews(affectedSheetIds.Count == 0 ? null : affectedSheetIds);
            if (_snapshot.HasDocument)
                _thumbnailCache.Invalidate(_snapshot.DocumentRuntimeSerialNumber,
                    affectedSheetIds.Count == 0 ? null : affectedSheetIds);
            _namedViewPreviewContentVersion++;
            _inspector.ClearNamedViewPreviews();
            _canvas.InvalidateNamedViewPreviews();
            ClearNamedViewPreviewRequests();
        }

        RefreshSnapshot(fit: invalidation.Kind.HasFlag(OverviewInvalidationKind.DocumentIdentity));
    }

    private void RefreshSnapshot(bool fit)
    {
        var previousNamedViews = _snapshot.NamedViews.ToArray();
        var previousSerial = _snapshot.DocumentRuntimeSerialNumber;
        RememberCurrentCamera(previousSerial);
        var next = LayoutFoundryUiHost.CaptureObserverSnapshot();
        var documentChanged = previousSerial != next.DocumentRuntimeSerialNumber;
        if (documentChanged)
        {
            ResetThumbnailCapture();
            _inspector.ClearNamedViewPreviews();
            _canvas.ReleasePreviews();
            if (previousSerial != 0) _thumbnailCache.Invalidate(previousSerial);
            _previewContentVersions.Clear();
            _previewContentSequence = 0;
            _namedViewPreviewContentVersion++;
        }

        var currentSheetIds = next.Sheets.Select(sheet => sheet.PageViewId).ToHashSet();
        foreach (var staleSheetId in _previewContentVersions.Keys
                     .Where(sheetId => !currentSheetIds.Contains(sheetId))
                     .ToArray())
            _previewContentVersions.Remove(staleSheetId);
        foreach (var sheetId in currentSheetIds)
            if (!_previewContentVersions.ContainsKey(sheetId))
                _previewContentVersions[sheetId] = ++_previewContentSequence;
        _snapshot = next with
        {
            Sheets = next.Sheets
                .Select(sheet => sheet with
                {
                    PreviewContentVersion = _previewContentVersions[sheet.PageViewId],
                })
                .ToArray(),
        };
        if (!previousNamedViews.SequenceEqual(_snapshot.NamedViews, StringComparer.OrdinalIgnoreCase))
        {
            _namedViewPreviewContentVersion++;
            _inspector.ClearNamedViewPreviews();
            _canvas.InvalidateNamedViewPreviews();
            ClearNamedViewPreviewRequests();
        }
        ObserverCamera? rememberedCamera = null;
        var hasRememberedCamera = _snapshot.HasDocument &&
                                  _documentCameras.TryGetValue(
                                      _snapshot.DocumentRuntimeSerialNumber,
                                      out rememberedCamera);
        _canvas.SetSnapshot(_snapshot, fit: false);
        if (hasRememberedCamera)
        {
            _pendingInitialFitDocumentSerial = null;
            _canvas.SetCamera(rememberedCamera!);
        }
        else if (_snapshot.HasDocument && (fit || documentChanged))
        {
            _pendingInitialFitDocumentSerial = _snapshot.DocumentRuntimeSerialNumber;
        }
        else if (!_snapshot.HasDocument)
        {
            _pendingInitialFitDocumentSerial = null;
        }
        var selectionMatchesDocument = LayoutFoundryUiHost.Selection.DocumentRuntimeSerialNumber ==
                                       (_snapshot.HasDocument
                                           ? _snapshot.DocumentRuntimeSerialNumber
                                           : null);
        _canvas.SetSelection(
            selectionMatchesDocument ? LayoutFoundryUiHost.Selection.Selected : [],
            selectionMatchesDocument ? LayoutFoundryUiHost.Selection.Anchor : null);
        PopulateNavigator();
        RefreshInspectorContext();
        // The shared Layout Foundry footer owns persistent document totals.
        // This local row is reserved for operation feedback and errors.
        _status.Text = string.Empty;
        if (_pendingInitialFitDocumentSerial is not null)
        {
            TryApplyPendingInitialFit(_canvasOverlay.ClientSize);
            QueueCanvasOverlayLayout();
        }
        QueueVisiblePreviews();
        QueueNamedViewPreviews();
    }

    private void RememberCurrentCamera(uint documentSerial)
    {
        if (!_isLoaded ||
            !_snapshot.HasDocument ||
            documentSerial == 0 ||
            _pendingInitialFitDocumentSerial == documentSerial ||
            !CanvasViewportIsReady(_canvasOverlay.ClientSize))
            return;

        _documentCameras[documentSerial] = _canvas.Camera;
    }

    private void TryApplyPendingInitialFit(Size viewportSize)
    {
        if (!_isLoaded ||
            _pendingInitialFitDocumentSerial is not { } documentSerial ||
            !_snapshot.HasDocument ||
            documentSerial != _snapshot.DocumentRuntimeSerialNumber ||
            !CanvasViewportIsReady(viewportSize) ||
            _canvas.BoardLayout.Bounds.IsEmpty)
            return;

        // Clear the pending marker first so the ViewChanged notification produced by
        // FitAll records this valid camera for subsequent visits to this document.
        _pendingInitialFitDocumentSerial = null;
        _canvas.FitAll();
    }

    private static bool CanvasViewportIsReady(Size viewportSize) =>
        viewportSize.Width >= MinimumInitialFitViewportDimension &&
        viewportSize.Height >= MinimumInitialFitViewportDimension;

    private void PopulateNavigator()
    {
        _navigatorRows = BuildNavigatorRows(_snapshot);
        _updatingNavigatorSelection = true;
        try
        {
            _navigator.DataStore = _navigatorRows;
            var selected = LayoutFoundryUiHost.Selection.Selected;
            _navigator.SelectedRows = _navigatorRows
                .Select((row, index) => (row, index))
                .Where(pair => selected.Contains(pair.row.Key))
                .Select(pair => pair.index)
                .ToArray();
        }
        finally
        {
            _updatingNavigatorSelection = false;
        }

        QueueCanvasOverlayLayout();
    }

    private void OnNavigatorSelectionChanged(object? sender, EventArgs eventArgs)
    {
        if (_updatingNavigatorSelection) return;
        var rows = _navigator.SelectedRows
            .Where(index => index >= 0 && index < _navigatorRows.Length)
            .Select(index => _navigatorRows[index])
            .ToArray();
        LayoutFoundryUiHost.Selection.Replace(
            _snapshot.HasDocument ? _snapshot.DocumentRuntimeSerialNumber : null,
            rows.Select(row => row.Key),
            rows.FirstOrDefault()?.Key,
            this);
    }

    private void OnSharedSelectionChanged(object? sender, DocumentSelectionChangedEventArgs eventArgs)
    {
        if (eventArgs.DocumentRuntimeSerialNumber !=
            (_snapshot.HasDocument ? _snapshot.DocumentRuntimeSerialNumber : null)) return;
        _canvas.SetSelection(eventArgs.Selection, eventArgs.Anchor);
        if (!ReferenceEquals(eventArgs.Source, this)) PopulateNavigator();
        RefreshInspectorContext();
    }

    private void RefreshInspectorContext()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        IEnumerable<OverviewNodeKey> selection = snapshot is not null &&
                        LayoutFoundryUiHost.Selection.DocumentRuntimeSerialNumber ==
                        snapshot.DocumentRuntimeSerialNumber
            ? LayoutFoundryUiHost.Selection.Selected
            : [];
        _inspector.SetContext(snapshot, selection);
    }

    private void QueueVisiblePreviews()
    {
        if (!_snapshot.HasDocument) return;
        var visibleWorld = _canvas.Camera.VisibleWorld(
            new ObserverSize(Math.Max(1, _canvas.Size.Width), Math.Max(1, _canvas.Size.Height)));
        var cards = _canvas.VisibleSheets(includeOverscan: true);
        _canvas.Invalidate();
        var retained = cards.Select(card => card.Sheet.PageViewId).ToHashSet();
        var contentVersions = _snapshot.Sheets.ToDictionary(
            sheet => sheet.PageViewId,
            sheet => sheet.PreviewContentVersion);
        var backgroundArgb = PreviewBackgroundArgb(FoundryTheme.CanvasPreviewBackground);
        _thumbnailQueue.RetainPending(key =>
            key.DocumentRuntimeSerialNumber == _snapshot.DocumentRuntimeSerialNumber &&
            retained.Contains(key.SheetPageViewId) &&
            contentVersions.TryGetValue(key.SheetPageViewId, out var contentVersion) &&
            contentVersion == key.ContentVersion &&
            key.BackgroundArgb == backgroundArgb);
        // Decoded bitmaps are held only for visible/overscan cards. Encoded PNGs
        // remain available in the bounded LRU cache for immediate return visits.
        _canvas.PrunePreviews(retained);
        foreach (var card in cards)
        {
            var longestPixels = Math.Max(card.Bounds.Width, card.Bounds.Height) * _canvas.Camera.Zoom;
            var currentBucket = _canvas.CurrentPreviewBucket(card.Sheet.PageViewId);
            var bucket = ObserverThumbnailResolution.Select(longestPixels, currentBucket);
            if (_canvas.HasCurrentPreview(card.Sheet.PageViewId, card.Sheet.PreviewContentVersion, bucket))
                continue;
            var (width, height) = PreviewDimensions(card, bucket);
            var key = new OverviewThumbnailKey(
                _snapshot.DocumentRuntimeSerialNumber,
                card.Sheet.PageViewId,
                width,
                height,
                card.Sheet.PreviewContentVersion,
                bucket,
                backgroundArgb);
            if (_thumbnailCache.TryGet(key, out var bytes))
            {
                _canvas.SetPreview(key, new Bitmap(bytes));
                continue;
            }

            var selected = LayoutFoundryUiHost.Selection.Selected.Contains(
                new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId));
            var matched = _filter.MatchesSheet(card.Sheet.PageViewId);
            var visible = card.Bounds.Intersects(visibleWorld);
            var priority = selected
                ? 0
                : matched && visible
                    ? 5
                    : visible
                        ? 10
                        : matched
                            ? 15
                            : 20;
            _thumbnailQueue.Enqueue(new OverviewThumbnailRequest(key, priority));
        }

        if (_thumbnailQueue.PendingCount > 0 && !_thumbnailTimer.Started)
            _thumbnailTimer.Start();
    }

    private async Task CaptureNextThumbnailAsync()
    {
        if (_thumbnailCaptureInProgress) return;
        var namedViewRequest = (_canvas.NamedViewsUseThumbnails || _inspector.UsesNamedViewThumbnails) &&
                               _namedViewThumbnailQueue.Count > 0
            ? _namedViewThumbnailQueue.Dequeue()
            : null;
        var request = namedViewRequest is null ? _thumbnailQueue.TakeNext() : null;
        if (request is null && namedViewRequest is null)
        {
            _thumbnailTimer.Stop();
            return;
        }

        _thumbnailCaptureInProgress = true;
        try
        {
            if (namedViewRequest is not null)
            {
                var namedResult = await LayoutFoundryUiHost.CaptureNamedViewThumbnailAsync(
                    namedViewRequest,
                    _thumbnailCancellation.Token);
                if (namedResult.Succeeded &&
                    namedViewRequest.Key.ContentVersion == _namedViewPreviewContentVersion &&
                    _snapshot.DocumentRuntimeSerialNumber == namedViewRequest.Key.DocumentRuntimeSerialNumber &&
                    _snapshot.NamedViews.Contains(namedViewRequest.Key.NamedViewName))
                {
                    var bitmap = new Bitmap(namedResult.PngBytes!);
                    _canvas.SetNamedViewPreview(
                        namedResult.Key.NamedViewName,
                        namedResult.Key.ContentVersion,
                        bitmap);
                    _inspector.SetNamedViewPreview(namedResult.Key.NamedViewName, bitmap);
                }
            }
            else if (request is not null)
            {
                var result = await LayoutFoundryUiHost.CaptureThumbnailAsync(
                    request,
                    _thumbnailCancellation.Token);
                var sheet = _snapshot.Sheets.FirstOrDefault(candidate =>
                    candidate.PageViewId == request.Key.SheetPageViewId);
                if (result.Succeeded &&
                    sheet is not null &&
                    sheet.PreviewContentVersion == request.Key.ContentVersion &&
                    _snapshot.DocumentRuntimeSerialNumber == request.Key.DocumentRuntimeSerialNumber &&
                    result.Key.BackgroundArgb == PreviewBackgroundArgb(FoundryTheme.CanvasPreviewBackground))
                {
                    _thumbnailCache.Store(result.Key, result.PngBytes!);
                    var retainDecoded = _canvas.VisibleSheets(includeOverscan: true)
                        .Any(card => card.Sheet.PageViewId == request.Key.SheetPageViewId);
                    if (retainDecoded)
                        _canvas.SetPreview(result.Key, new Bitmap(result.PngBytes!));
                }
            }
        }
        finally
        {
            if (request is not null) _thumbnailQueue.Complete(request.Key);
            if (namedViewRequest is not null)
                _pendingNamedViewThumbnails.Remove(namedViewRequest.Key);
            _thumbnailCaptureInProgress = false;
        }
    }

    private static uint PreviewBackgroundArgb(Color color) =>
        ((uint)color.Ab << 24) |
        ((uint)color.Rb << 16) |
        ((uint)color.Gb << 8) |
        (uint)color.Bb;

    private void QueueNamedViewPreviews()
    {
        if (!_snapshot.HasDocument ||
            (!_canvas.NamedViewsUseThumbnails && !_inspector.UsesNamedViewThumbnails)) return;
        foreach (var name in _canvas.VisibleNamedViews()
                     .Concat(_inspector.VisibleNamedViews())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_canvas.HasNamedViewPreview(name, _namedViewPreviewContentVersion) ||
                _inspector.HasNamedViewPreview(name)) continue;
            var key = new NamedViewThumbnailKey(
                _snapshot.DocumentRuntimeSerialNumber,
                name,
                192,
                120,
                _namedViewPreviewContentVersion);
            if (!_pendingNamedViewThumbnails.Add(key)) continue;
            _namedViewThumbnailQueue.Enqueue(new NamedViewThumbnailRequest(key));
        }

        if (_namedViewThumbnailQueue.Count > 0 && !_thumbnailTimer.Started)
            _thumbnailTimer.Start();
    }

    private void ClearNamedViewPreviewRequests()
    {
        _namedViewThumbnailQueue.Clear();
        _pendingNamedViewThumbnails.Clear();
    }

    private void ResetThumbnailCapture()
    {
        _thumbnailTimer.Stop();
        _thumbnailCancellation.Cancel();
        _thumbnailCancellation.Dispose();
        _thumbnailCancellation = new CancellationTokenSource();
        _thumbnailQueue.Clear();
        ClearNamedViewPreviewRequests();
        _thumbnailCaptureInProgress = false;
    }

    private async Task ApplyBoardStateAsync(ObserverCanvasState state, string undoDescription)
    {
        var result = await LayoutFoundryUiHost.SetObserverCanvasStateAsync(state, undoDescription);
        _status.Text = ResultMessage(result, "Observer board updated.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private async Task TidyAsync()
    {
        if (!_snapshot.HasDocument) return;
        if (_canvas.PackingMode == ObserverPackingMode.CompactSheets)
        {
            _canvas.FitAll();
            _status.Text = "Compact packing is already automatically tidy.";
            return;
        }
        var selected = LayoutFoundryUiHost.Selection.Selected;
        var sheetIds = selected.Where(key => key.Kind == OverviewNodeKind.Sheet).Select(key => key.Id).ToHashSet();
        var folderIds = selected.Where(key => key.Kind == OverviewNodeKind.Folder).Select(key => key.Id).ToHashSet();
        var stateIds = selected.Where(key => key.Kind == OverviewNodeKind.AppearanceState).Select(key => key.Id).ToHashSet();
        var state = new ObserverPlacementPlanner().Tidy(
            _snapshot,
            selected.Count == 0 ? null : sheetIds,
            selected.Count == 0 ? null : folderIds,
            selected.Count == 0 ? null : stateIds);
        if (ObserverCanvasStateComparer.ContentEquals(state, _snapshot.CanvasState))
        {
            _status.Text = "The selected board area is already tidy.";
            return;
        }

        await ApplyBoardStateAsync(state, selected.Count == 0 ? "Tidy observer canvas" : "Tidy observer selection");
    }

    private async Task MoveHierarchyAsync(ObserverHierarchyMoveRequestedEventArgs eventArgs)
    {
        OperationResult result;
        if (eventArgs.AppearanceStateIds.Count > 0)
            result = await LayoutFoundryUiHost.MoveAppearanceStatesAsync(
                eventArgs.AppearanceStateIds, eventArgs.DestinationFolderId);
        else if (eventArgs.SheetIds.Count > 0)
            result = await LayoutFoundryUiHost.MoveSheetsAsync(eventArgs.DestinationFolderId, eventArgs.SheetIds);
        else
            result = await LayoutFoundryUiHost.MoveFoldersAsync(eventArgs.DestinationFolderId, eventArgs.FolderIds);
        _status.Text = ResultMessage(result, "Hierarchy updated.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private async Task ReorganizeHierarchyAsync(ObserverHierarchyPlacementRequestedEventArgs eventArgs)
    {
        var result = await LayoutFoundryUiHost.ReorganizeHierarchyAsync(
            eventArgs.FolderIds,
            eventArgs.SheetIds,
            eventArgs.Target);
        _status.Text = ResultMessage(result, "Hierarchy updated.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private async Task AssignNamedViewAsync(ObserverNamedViewRequestedEventArgs eventArgs)
    {
        var result = await LayoutFoundryUiHost.AssignNamedViewAsync(
            eventArgs.DetailViewportIds,
            eventArgs.NamedViewName);
        _status.Text = ResultMessage(result,
            $"Assigned {eventArgs.NamedViewName} to {eventArgs.DetailViewportIds.Count} detail{(eventArgs.DetailViewportIds.Count == 1 ? string.Empty : "s")}.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private async Task AssignSelectedNamedViewAsync(string namedViewName)
    {
        if (string.IsNullOrWhiteSpace(namedViewName))
        {
            _status.Text = "Choose a named view first.";
            return;
        }

        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null) return;
        var detailIds = BatchTargetResolver.ResolveDetailIds(
            snapshot,
            LayoutFoundryUiHost.Selection.Selected);
        if (detailIds.Count == 0)
        {
            _status.Text = "Select at least one detail, layout, or folder containing details.";
            return;
        }

        await AssignNamedViewAsync(new ObserverNamedViewRequestedEventArgs(namedViewName, detailIds));
    }

    private void Navigate(OverviewNavigationTarget target)
    {
        var result = LayoutFoundryUiHost.Navigate(target);
        _status.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private void NavigateSelection()
    {
        var key = LayoutFoundryUiHost.Selection.Selected.Take(2).ToArray();
        if (key.Length != 1)
        {
            _status.Text = "Select one layout or detail to open it in Rhino.";
            return;
        }

        if (key[0].Kind == OverviewNodeKind.Sheet)
        {
            Navigate(new OverviewNavigationTarget(key[0].Id));
        }
        else if (key[0].Kind == OverviewNodeKind.Detail)
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == key[0].Id));
            if (owner is not null) Navigate(new OverviewNavigationTarget(owner.PageViewId, key[0].Id));
        }
    }

    private async void OpenBatchProperties()
    {
        var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
        if (snapshot is null) return;
        var selection = LayoutFoundryUiHost.Selection.Selected.ToArray();
        if (selection.Any(key => key.Kind == OverviewNodeKind.Detail))
        {
            var detailIds = BatchTargetResolver.ResolveDetailIds(snapshot, selection);
            if (detailIds.Count == 0)
            {
                _status.Text = "The selection does not contain any detail viewports.";
                return;
            }

            var detailDialog = new DetailPropertiesDialog(snapshot, detailIds);
            detailDialog.ShowModal(this);
            if (detailDialog.Succeeded) RefreshSnapshot(fit: false);
            return;
        }

        var targets = BatchTargetResolver.Resolve(snapshot, selection);
        if (targets.Count == 0)
        {
            _status.Text = "The selection does not contain any layouts.";
            return;
        }

        var dialog = new BatchCreateLayoutsDialog(snapshot, targets);
        dialog.ShowModal(this);
        await dialog.PreviewCleanup;
        if (dialog.Succeeded) RefreshSnapshot(fit: false);
    }

    private void ShowContextMenu(
        PointF location,
        Guid? pasteDestinationFolderId,
        ObserverPointRecord pasteTargetOrigin)
    {
        var selection = LayoutFoundryUiHost.Selection.Selected.ToArray();
        var open = new ButtonMenuItem { Text = "Open in Rhino" };
        var properties = new ButtonMenuItem { Text = "Batch Properties…" };
        var duplicate = new ButtonMenuItem { Text = selection.Length > 1 ? "Duplicate Items" : "Duplicate" };
        var delete = new ButtonMenuItem { Text = selection.Length > 1 ? "Delete Items…" : "Delete…" };
        var copy = new ButtonMenuItem { Text = "Copy" };
        var paste = new ButtonMenuItem { Text = "Paste" };
        var include = new ButtonMenuItem { Text = "Enable for Printing" };
        var exclude = new ButtonMenuItem { Text = "Disable from Printing" };
        var move = BuildMoveMenu(selection);
        var moveEarlier = new ButtonMenuItem { Text = "Move Earlier" };
        var moveLater = new ButtonMenuItem { Text = "Move Later" };
        var tidy = new ButtonMenuItem { Text = selection.Length == 0 ? "Tidy All" : "Tidy Selection" };
        var print = new ButtonMenuItem
        {
            Text = selection.Length == 1 && selection[0].Kind == OverviewNodeKind.Folder
                ? "Print Folder…"
                : selection.Length == 0 ? "Print Enabled…" : "Print Selection…",
        };
        open.Enabled = selection.Length == 1;
        properties.Enabled = selection.Length > 0;
        duplicate.Enabled = selection.Length > 0 && selection.All(key => key.Kind is
            OverviewNodeKind.Folder or OverviewNodeKind.Sheet);
        delete.Enabled = selection.Length > 0 && selection.All(key => key.Kind != OverviewNodeKind.Detail);
        copy.Enabled = selection.Length > 0 &&
                       selection.All(key => !(key.Kind == OverviewNodeKind.Folder && key.Id == _snapshot.RootFolderId));
        paste.Enabled = HierarchyClipboard.CanPasteCurrentDocument();
        include.Enabled = selection.Length > 0 && selection.Any(key => key.Kind is
            OverviewNodeKind.Folder or OverviewNodeKind.Sheet or OverviewNodeKind.Detail);
        exclude.Enabled = include.Enabled;
        moveEarlier.Enabled = moveLater.Enabled = selection.Length == 1 &&
            selection[0].Kind == OverviewNodeKind.Sheet;
        open.Click += (_, _) => NavigateSelection();
        properties.Click += (_, _) => OpenSelectionProperties();
        duplicate.Click += async (_, _) => await DuplicateSelectionAsync();
        delete.Click += (_, _) => RequestDeleteSelection();
        copy.Click += (_, _) => CopySelection();
        paste.Click += async (_, _) => await PasteSelectionAsync(
            pasteDestinationFolderId ?? _snapshot.RootFolderId,
            pasteTargetOrigin);
        include.Click += async (_, _) => await SetPrintInclusionAsync(true);
        exclude.Click += async (_, _) => await SetPrintInclusionAsync(false);
        moveEarlier.Click += async (_, _) => await ReorderSelectionByStepAsync(-1);
        moveLater.Click += async (_, _) => await ReorderSelectionByStepAsync(1);
        tidy.Click += async (_, _) => await TidyAsync();
        print.Click += (_, _) => PrintSelection();
        var menu = new ContextMenu(
            open,
            properties,
            new SeparatorMenuItem(),
            copy,
            paste,
            new SeparatorMenuItem(),
            duplicate,
            delete,
            move,
            moveEarlier,
            moveLater,
            new SeparatorMenuItem(),
            include,
            exclude,
            print,
            new SeparatorMenuItem(),
            tidy);
        menu.Show(_canvas, location);
    }

    private ButtonMenuItem BuildMoveMenu(IReadOnlyList<OverviewNodeKey> selection)
    {
        var move = new ButtonMenuItem { Text = "Move to Folder" };
        move.Enabled = selection.Any(key =>
            key.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet or OverviewNodeKind.Detail or
                OverviewNodeKind.AppearanceState);
        foreach (var folder in _snapshot.Folders
                     .OrderBy(folder => FolderDepth(folder.Id))
                     .ThenBy(folder => folder.Order)
                     .ThenBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase))
        {
            var target = new ButtonMenuItem
            {
                Text = $"{new string(' ', FolderDepth(folder.Id) * 2)}{folder.Name}",
            };
            target.Click += async (_, _) => await MoveSelectionToFolderAsync(folder.Id);
            move.Items.Add(target);
        }

        return move;
    }

    private async Task MoveSelectionToFolderAsync(Guid destinationFolderId)
    {
        var selection = LayoutFoundryUiHost.Selection.Selected.ToArray();
        var folderIds = selection
            .Where(key => key.Kind == OverviewNodeKind.Folder && key.Id != _snapshot.RootFolderId)
            .Select(key => key.Id)
            .ToArray();
        var sheetIds = selection
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Select(key => key.Id)
            .ToHashSet();
        foreach (var detailId in selection.Where(key => key.Kind == OverviewNodeKind.Detail).Select(key => key.Id))
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == detailId));
            if (owner is not null) sheetIds.Add(owner.PageViewId);
        }

        var stateIds = selection.Where(key => key.Kind == OverviewNodeKind.AppearanceState)
            .Select(key => key.Id).ToArray();

        if (stateIds.Length > 0)
        {
            var stateResult = await LayoutFoundryUiHost.MoveAppearanceStatesAsync(
                stateIds, destinationFolderId);
            if (!stateResult.Succeeded)
            {
                _status.Text = ResultMessage(stateResult, string.Empty);
                return;
            }
        }

        if (folderIds.Length == 0 && sheetIds.Count == 0)
        {
            _status.Text = "Appearance state moved to folder.";
            RefreshSnapshot(fit: false);
            return;
        }

        var result = await LayoutFoundryUiHost.MoveHierarchySelectionAsync(
            destinationFolderId,
            folderIds,
            sheetIds.ToArray());
        _status.Text = ResultMessage(result, "Selection moved to folder.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private void OpenSelectionProperties()
    {
        var selection = LayoutFoundryUiHost.Selection.Selected.ToArray();
        if (selection.Length == 1 && selection[0].Kind is
                OverviewNodeKind.AppearanceState)
        {
            var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
            var state = snapshot?.AppearanceStates.FirstOrDefault(item => item.Id == selection[0].Id);
            if (snapshot is null || state is null) return;
            var dialog = AppearanceStateEditorDialog.ShowWithViewportPicking(this, snapshot, state);
            if (dialog.Changed) RefreshSnapshot(fit: false);
            return;
        }
        OpenBatchProperties();
    }

    private async Task ReorderSelectionByStepAsync(int direction)
    {
        var selected = LayoutFoundryUiHost.Selection.Selected
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Take(2)
            .ToArray();
        if (selected.Length != 1)
        {
            _status.Text = "Select exactly one layout to change its order.";
            return;
        }

        var moving = _snapshot.Sheets.FirstOrDefault(sheet => sheet.PageViewId == selected[0].Id);
        if (moving is null) return;
        var siblings = _snapshot.Sheets
            .Where(sheet => sheet.FolderId == moving.FolderId)
            .OrderBy(sheet => sheet.Order)
            .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var index = Array.FindIndex(siblings, sheet => sheet.PageViewId == moving.PageViewId);
        if (index < 0 || direction < 0 && index == 0 || direction > 0 && index == siblings.Length - 1)
        {
            _status.Text = "The layout is already at the edge of its folder.";
            return;
        }

        Guid? beforeId = direction < 0
            ? siblings[index - 1].PageViewId
            : index + 2 < siblings.Length ? siblings[index + 2].PageViewId : null;
        var result = await LayoutFoundryUiHost.ReorderSheetAsync(moving.PageViewId, beforeId);
        _status.Text = ResultMessage(result, "Layout order updated.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private int FolderDepth(Guid folderId)
    {
        var folders = _snapshot.Folders.ToDictionary(folder => folder.Id);
        var depth = 0;
        var visited = new HashSet<Guid>();
        Guid? current = folderId;
        while (current is { } id && visited.Add(id) && folders.TryGetValue(id, out var folder) &&
               folder.ParentId is { } parent)
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    private async Task DuplicateSelectionAsync()
    {
        var selection = LayoutFoundryUiHost.Selection.Selected
            .Where(key => key.Kind != OverviewNodeKind.Detail)
            .ToArray();
        if (selection.Length == 0) return;
        var result = await LayoutFoundryUiHost.DuplicateSelectionAsync(selection);
        _status.Text = ResultMessage(result, $"Duplicated {selection.Length} item{(selection.Length == 1 ? string.Empty : "s")}.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private void CopySelection()
    {
        _status.Text = HierarchyClipboard.CopyCurrentSelection().Message;
    }

    private async Task PasteSelectionAsync(
        Guid? destinationFolderId = null,
        ObserverPointRecord? targetOrigin = null)
    {
        var result = await HierarchyClipboard.PasteAsync(destinationFolderId, targetOrigin);
        _status.Text = result.Message;
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }


    private void RequestDeleteSelection()
    {
        var selection = LayoutFoundryUiHost.Selection.Selected
            .Where(key => key.Kind != OverviewNodeKind.Detail &&
                          !(key.Kind == OverviewNodeKind.Folder && key.Id == _snapshot.RootFolderId))
            .ToArray();
        if (selection.Length == 0) return;
        DeleteSelectionRequested?.Invoke(
            this,
            new DeleteSelectionRequestedEventArgs(selection));
    }

    private async Task SetPrintInclusionAsync(bool include)
    {
        var result = await LayoutFoundryUiHost.SetPrintInclusionAsync(
            LayoutFoundryUiHost.Selection.Selected.ToArray(),
            include);
        _status.Text = ResultMessage(result,
            include ? "Enabled for printing." : "Disabled from printing.");
        if (result.Succeeded) RefreshSnapshot(fit: false);
    }

    private void PrintSelection()
    {
        var overview = LayoutFoundryUiHost.CaptureOverview();
        var selection = LayoutFoundryUiHost.Selection.Selected.ToArray();
        Guid? folderId = selection.Length == 1 && selection[0].Kind == OverviewNodeKind.Folder
            ? selection[0].Id
            : null;
        IReadOnlyList<Guid> sheetIds;
        if (selection.Length == 0 || folderId is not null)
        {
            var scope = LayoutPrintScopeResolver.Resolve(overview, folderId);
            sheetIds = scope.SheetPageViewIds;
        }
        else
        {
            var snapshot = LayoutFoundryUiHost.CaptureSnapshot();
            sheetIds = snapshot is null
                ? []
                : BatchTargetResolver.ResolveSheetIds(snapshot, selection)
                    .Where(id => snapshot.Sheets[id].IncludeInPrintAll)
                    .ToArray();
        }

        if (overview.DocumentRuntimeSerialNumber is not { } serial || sheetIds.Count == 0)
        {
            _status.Text = "There are no included layouts to print.";
            return;
        }

        var result = LayoutFoundryUiHost.ShowPrintDialog(new LayoutPrintDialogRequest(
            serial,
            sheetIds,
            folderId is null ? "Print Enabled Layouts" : "Print Enabled Folder Layouts"));
        _status.Text = result.Succeeded ? string.Empty : result.Message;
    }

    private IReadOnlySet<Guid> ResolveAffectedSheetIds(IReadOnlySet<Guid>? entityIds)
    {
        if (entityIds is null || entityIds.Count == 0) return new HashSet<Guid>();
        return _snapshot.Sheets
            .Where(sheet => entityIds.Contains(sheet.PageViewId) ||
                            sheet.Details.Any(detail => entityIds.Contains(detail.DetailViewportId)))
            .Select(sheet => sheet.PageViewId)
            .ToHashSet();
    }

    private void AdvancePreviewVersions(IReadOnlySet<Guid>? sheetIds)
    {
        var targets = sheetIds is null || sheetIds.Count == 0
            ? _snapshot.Sheets.Select(sheet => sheet.PageViewId)
            : sheetIds;
        foreach (var sheetId in targets)
            _previewContentVersions[sheetId] = ++_previewContentSequence;
    }

    private static (int Width, int Height) PreviewDimensions(ObserverSheetCard card, int bucket)
    {
        var width = Math.Max(1, card.Bounds.Width);
        var height = Math.Max(1, card.Bounds.Height);
        return width >= height
            ? (bucket, Math.Max(1, (int)Math.Round(bucket * height / width)))
            : (Math.Max(1, (int)Math.Round(bucket * width / height)), bucket);
    }

    private static string ResultMessage(OperationResult result, string success) =>
        result.Succeeded
            ? success
            : result.Diagnostics.FirstOrDefault()?.Message ?? "The operation could not be completed.";

    private static NavigatorRow[] BuildNavigatorRows(ObserverSnapshot snapshot)
    {
        if (!snapshot.HasDocument) return [];
        var folders = snapshot.Folders.ToDictionary(folder => folder.Id);
        if (!folders.TryGetValue(snapshot.RootFolderId, out var root)) return [];
        var rows = new List<NavigatorRow>();
        var visited = new HashSet<Guid>();

        void AddFolder(ObserverFolderSnapshot folder, int depth)
        {
            if (!visited.Add(folder.Id)) return;
            rows.Add(new NavigatorRow(
                new OverviewNodeKey(OverviewNodeKind.Folder, folder.Id),
                $"{new string(' ', depth * 3)}📁  {folder.Name}"));
            foreach (var child in folders.Values.Where(candidate => candidate.ParentId == folder.Id)
                         .OrderBy(candidate => candidate.Order)
                         .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
                AddFolder(child, depth + 1);
            foreach (var state in snapshot.AppearanceStates.Where(state => state.FolderId == folder.Id)
                         .OrderBy(state => state.Order)
                         .ThenBy(state => state.Name, StringComparer.OrdinalIgnoreCase))
                rows.Add(new NavigatorRow(
                    new OverviewNodeKey(OverviewNodeKind.AppearanceState, state.Id),
                    $"{new string(' ', (depth + 1) * 3)}◫  {state.Name}"));
            foreach (var sheet in snapshot.Sheets.Where(sheet => sheet.FolderId == folder.Id)
                         .OrderBy(sheet => sheet.Order)
                         .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new NavigatorRow(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId),
                    $"{new string(' ', (depth + 1) * 3)}▣  {sheet.Name}"));
                foreach (var detail in sheet.Details)
                    rows.Add(new NavigatorRow(
                        new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId),
                        $"{new string(' ', (depth + 2) * 3)}⌗  {detail.Name}"));
            }
        }

        AddFolder(root, 0);
        return rows.ToArray();
    }

    private sealed record NavigatorRow(OverviewNodeKey Key, string Label);
}
