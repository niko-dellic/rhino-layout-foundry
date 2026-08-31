using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Operations;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed partial class ObserverCanvasDrawable : Drawable
{
    internal const string NamedViewDragType = "application/x-layout-foundry-named-view";
    private const double RightPanActivationDistance = 5;
    private const double NavigatorDragActivationDistance = 6;
    private const int NavigatorAutoScrollEdge = 28;
    private static readonly TimeSpan NavigatorHoverExpandDelay = TimeSpan.FromMilliseconds(650);
    private const int NavigatorWidth = 260;
    private const int NavigatorTop = 38;
    private const int NavigatorHeaderHeight = 0;
    private const int NavigatorRowHeight = 24;
    private const int NamedViewsWidth = 224;
    private const int NamedViewsTop = 38;
    private const int NamedViewsHeaderHeight = 28;
    private const int NamedViewsRowHeight = 24;
    private const int NamedViewsActionHeight = 30;
    private const int NamedViewsThumbnailColumns = 2;
    private const int NamedViewsThumbnailCardHeight = 92;
    private const double CameraInputFrameInterval = 1d / 60d;
    private const double CameraInputSettleInterval = 0.08;
    private const double WheelZoomSensitivity = 0.115;
    private readonly Font _folderFont = SystemFonts.Bold(11);
    private readonly Font _sheetFont = SystemFonts.Bold(10);
    private readonly Font _smallFont = SystemFonts.Default(8);
    private readonly Dictionary<Guid, PreviewEntry> _previews = [];
    private readonly Dictionary<string, NamedViewPreviewEntry> _namedViewPreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly UITimer _navigatorDragTimer;
    private readonly UITimer _navigatorHoverTimer;
    private readonly UITimer _cameraInputFrameTimer;
    private readonly UITimer _cameraInputSettleTimer;
    private ObserverSnapshot _snapshot = ObserverSnapshot.NoDocument;
    private OverviewFilterProjection _filter = new(false, new HashSet<OverviewNodeKey>(), new HashSet<Guid>());
    private ObserverBoardLayout _layout = ObserverBoardLayout.Empty;
    private ObserverSpatialIndex _spatialIndex = new(ObserverBoardLayout.Empty);
    private readonly ObserverCanvasLodPolicy _lodPolicy = new();
    private ObserverCanvasPresentation _presentation = ObserverCanvasPresentation.Empty;
    private HashSet<Guid> _drawableFolderIds = [];
    private ObserverPackingMode _packingMode = ObserverPackingMode.NestedFolders;
    private ObserverCamera _camera = ObserverCamera.Default;
    private Color _gridColor = FoundryTheme.CanvasGridColor;
    private double _gridOpacity = FoundryTheme.DefaultCanvasGridOpacity;
    private HashSet<OverviewNodeKey> _selection = [];
    private OverviewNodeKey? _selectionAnchor;
    private DragMode _dragMode;
    private ObserverPoint _pressScreen;
    private ObserverPoint _pressWorld;
    private ObserverPoint _lastScreen;
    private ObserverPoint _dragWorldDelta;
    private Guid? _dragFolderId;
    private Guid? _reorderSheetId;
    private ObserverRect? _lassoWorld;
    private ObserverPoint _contextWorld;
    private bool _spaceHeld;
    private bool _navigatorVisible = true;
    private bool _namedViewsVisible;
    private bool _namedViewsThumbnailMode;
    private CanvasNavigatorRow[] _navigatorRows = [];
    private int _navigatorScrollRow;
    private CanvasNavigatorRow? _navigatorPressRow;
    private OverviewNodeKey[] _navigatorDragKeys = [];
    private bool _navigatorCollapseSelectionOnMouseUp;
    private NavigatorDropResolution? _navigatorDrop;
    private Guid? _navigatorHoverFolderId;
    private DateTime _navigatorHoverStartedUtc;
    private DateTime _navigatorLastAutoScrollUtc;
    private PointF _navigatorPointer;
    private NavigatorFolderDraft? _navigatorFolderDraft;
    private readonly HashSet<Guid> _collapsedNavigatorFolders = [];
    private readonly HashSet<Guid> _expandedNavigatorSheets = [];
    private int _namedViewsScrollRow;
    private string? _selectedNamedView;
    private string? _dragNamedView;
    private Guid? _hoverDetailId;
    private bool _hasCanvasPastePoint;
    private ObserverPoint _pendingPanScreen;
    private ObserverPoint _pendingZoomAnchorScreen;
    private double _pendingZoomFactor = 1;
    private bool _cameraInputFrameScheduled;

    internal ObserverCanvasDrawable()
        : base(true)
    {
        BackgroundColor = FoundryTheme.CanvasBackground;
        CanFocus = true;
        AllowDrop = true;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => SetHoveredDetail(null);
        MouseUp += OnMouseUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        TextInput += OnTextInput;
        DragOver += OnDragOver;
        DragDrop += OnDragDrop;
        SizeChanged += (_, _) => RefreshPresentation();
        LoadComplete += (_, _) => AttachNativeTrackpadInput();
        UnLoad += (_, _) =>
        {
            DetachNativeTrackpadInput();
            StopQueuedCameraInput();
        };
        _navigatorDragTimer = new UITimer { Interval = 0.07 };
        _navigatorDragTimer.Elapsed += (_, _) =>
        {
            if (_dragMode != DragMode.Navigator)
            {
                _navigatorDragTimer.Stop();
                return;
            }
            AutoScrollNavigator(_navigatorPointer);
            _navigatorDrop = ResolveNavigatorDrop(_navigatorPointer);
            UpdateNavigatorHoverExpansion();
            Invalidate();
        };
        _navigatorHoverTimer = new UITimer { Interval = NavigatorHoverExpandDelay.TotalSeconds };
        _navigatorHoverTimer.Elapsed += (_, _) =>
        {
            _navigatorHoverTimer.Stop();
            if (_dragMode != DragMode.Navigator || _navigatorHoverFolderId is not { } folderId ||
                !_collapsedNavigatorFolders.Remove(folderId)) return;
            _navigatorRows = BuildNavigatorRows(_snapshot);
            ClampNavigatorScroll();
            _navigatorDrop = ResolveNavigatorDrop(_navigatorPointer);
            Invalidate();
        };
        _cameraInputFrameTimer = new UITimer { Interval = CameraInputFrameInterval };
        _cameraInputFrameTimer.Elapsed += (_, _) => FlushQueuedCameraInput();
        _cameraInputSettleTimer = new UITimer { Interval = CameraInputSettleInterval };
        _cameraInputSettleTimer.Elapsed += (_, _) =>
        {
            _cameraInputSettleTimer.Stop();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    internal event EventHandler? ViewChanged;
    internal event EventHandler<ObserverBoardStateRequestedEventArgs>? BoardStateRequested;
    internal event EventHandler<ObserverSelectionRequestedEventArgs>? SelectionRequested;
    internal event EventHandler<ObserverNavigationRequestedEventArgs>? NavigationRequested;
    internal event EventHandler<ObserverHierarchyMoveRequestedEventArgs>? HierarchyMoveRequested;
    internal event EventHandler<ObserverHierarchyPlacementRequestedEventArgs>? HierarchyPlacementRequested;
    internal event EventHandler<ObserverReorderRequestedEventArgs>? ReorderRequested;
    internal event EventHandler<ObserverReorderStepRequestedEventArgs>? ReorderStepRequested;
    internal event EventHandler<ObserverNamedViewRequestedEventArgs>? NamedViewRequested;
    internal event EventHandler<ObserverNamedViewSelectionRequestedEventArgs>? AssignNamedViewToSelectionRequested;
    internal event EventHandler? NamedViewPreviewsRequested;
    internal event EventHandler<ObserverContextRequestedEventArgs>? ContextRequested;
    internal event EventHandler? DeleteRequested;
    internal event EventHandler? TidyRequested;
    internal event EventHandler? ExitWorkspaceRequested;
    internal event EventHandler<ObserverFolderDraftRequestedEventArgs>? FolderDraftRequested;
    internal event EventHandler? CopyRequested;
    internal event EventHandler<ObserverPasteRequestedEventArgs>? PasteRequested;

    internal ObserverCamera Camera => _camera;
    internal ObserverBoardLayout BoardLayout => _layout;
    internal ObserverSnapshot Snapshot => _snapshot;
    internal ObserverPackingMode PackingMode => _packingMode;
    internal ObserverCanvasPresentation Presentation => _presentation;
    internal Color GridColor => _gridColor;
    internal double GridOpacity => _gridOpacity;
    internal bool ExitWorkspaceOnEscape { get; set; }

    internal void SetGridAppearance(Color color, double opacity)
    {
        _gridColor = Color.FromArgb(color.Rb, color.Gb, color.Bb, 255);
        _gridOpacity = Math.Clamp(opacity, 0, 1);
        Invalidate();
    }

    internal void SetPackingMode(ObserverPackingMode packingMode, bool fit)
    {
        if (_packingMode == packingMode) return;
        _packingMode = packingMode;
        _layout = new ObserverPlacementPlanner().Arrange(_snapshot, _packingMode);
        _spatialIndex = new ObserverSpatialIndex(_layout);
        RefreshPresentation();
        if (fit && !_layout.Bounds.IsEmpty) FitAll();
        else Invalidate();
    }

    internal void SetSnapshot(ObserverSnapshot snapshot, bool fit)
    {
        _snapshot = snapshot ?? ObserverSnapshot.NoDocument;
        _layout = new ObserverPlacementPlanner().Arrange(_snapshot, _packingMode);
        _spatialIndex = new ObserverSpatialIndex(_layout);
        RefreshPresentation();
        var folderIds = _snapshot.Folders.Select(folder => folder.Id).ToHashSet();
        _collapsedNavigatorFolders.RemoveWhere(id => !folderIds.Contains(id));
        var sheetIds = _snapshot.Sheets.Select(sheet => sheet.PageViewId).ToHashSet();
        _expandedNavigatorSheets.RemoveWhere(id => !sheetIds.Contains(id));
        _navigatorRows = BuildNavigatorRows(_snapshot);
        if (_selectedNamedView is null || !_snapshot.NamedViews.Contains(_selectedNamedView))
            _selectedNamedView = _snapshot.NamedViews.FirstOrDefault();
        foreach (var stale in _namedViewPreviews.Keys
                     .Where(name => !_snapshot.NamedViews.Contains(name))
                     .ToArray())
        {
            _namedViewPreviews[stale].Bitmap.Dispose();
            _namedViewPreviews.Remove(stale);
        }
        if (_navigatorFolderDraft is { } draft &&
            (!_snapshot.Folders.Any(folder => folder.Id == draft.ParentFolderId) ||
             _snapshot.Folders.Any(folder => folder.Id == draft.Id)))
            _navigatorFolderDraft = null;
        ClampNavigatorScroll();
        _selection.RemoveWhere(key => !ContainsKey(key));
        if (_selectionAnchor is { } anchor && !_selection.Contains(anchor))
            _selectionAnchor = _selection.Count == 1 ? _selection.Single() : null;
        if (fit && !_layout.Bounds.IsEmpty)
        {
            FitAll();
        }
        else
        {
            Invalidate();
        }
    }

    internal void SetFilter(OverviewFilterProjection projection)
    {
        _filter = projection ?? throw new ArgumentNullException(nameof(projection));
        Invalidate();
    }

    internal void SetNavigatorVisible(bool visible)
    {
        _navigatorVisible = visible;
        Invalidate();
    }

    internal void SetNamedViewsVisible(bool visible)
    {
        _namedViewsVisible = visible;
        ClampNamedViewsScroll();
        Invalidate();
        if (visible && _namedViewsThumbnailMode)
            NamedViewPreviewsRequested?.Invoke(this, EventArgs.Empty);
    }

    internal bool NamedViewsUseThumbnails => _namedViewsVisible && _namedViewsThumbnailMode;

    internal IReadOnlyList<string> VisibleNamedViews()
    {
        if (!NamedViewsUseThumbnails) return [];
        return _snapshot.NamedViews
            .Skip(_namedViewsScrollRow)
            .Take(NamedViewsVisibleItemCount(ViewportSize()))
            .ToArray();
    }

    internal bool HasNamedViewPreview(string name, long contentVersion) =>
        _namedViewPreviews.TryGetValue(name, out var preview) &&
        preview.ContentVersion == contentVersion;

    internal void SetNamedViewPreview(string name, long contentVersion, Bitmap bitmap)
    {
        if (_namedViewPreviews.Remove(name, out var previous) &&
            !ReferenceEquals(previous.Bitmap, bitmap))
            previous.Bitmap.Dispose();
        _namedViewPreviews[name] = new NamedViewPreviewEntry(contentVersion, bitmap);
        Invalidate();
    }

    internal void InvalidateNamedViewPreviews()
    {
        foreach (var preview in _namedViewPreviews.Values) preview.Bitmap.Dispose();
        _namedViewPreviews.Clear();
        Invalidate();
    }

    internal void BeginNavigatorFolderDraft(Guid parentFolderId)
    {
        if (!_snapshot.Folders.Any(folder => folder.Id == parentFolderId)) return;
        _navigatorVisible = true;
        ExpandNavigatorFolderPath(parentFolderId);
        _navigatorRows = BuildNavigatorRows(_snapshot);
        _navigatorFolderDraft = new NavigatorFolderDraft(
            Guid.NewGuid(),
            parentFolderId,
            "New Folder",
            SelectAll: true);
        EnsureNavigatorDraftVisible();
        Focus();
        Invalidate();
    }

    internal void CancelNavigatorFolderDraft()
    {
        _navigatorFolderDraft = null;
        Invalidate();
    }

    internal void SetSelection(
        IEnumerable<OverviewNodeKey> selection,
        OverviewNodeKey? anchor = null)
    {
        _selection = selection.Where(ContainsKey).ToHashSet();
        _selectionAnchor = anchor is { } candidate && _selection.Contains(candidate)
            ? candidate
            : _selection.Count == 1
                ? _selection.Single()
                : null;
        foreach (var detailId in _selection
                     .Where(key => key.Kind == OverviewNodeKind.Detail)
                     .Select(key => key.Id))
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == detailId));
            if (owner is not null) _expandedNavigatorSheets.Add(owner.PageViewId);
        }
        _navigatorRows = BuildNavigatorRows(_snapshot);
        ClampNavigatorScroll();
        Invalidate();
    }

    internal void FitAll()
    {
        _camera = ObserverCamera.Fit(_layout.Bounds, ViewportSize());
        NotifyViewChanged();
    }

    internal void FocusSelection()
    {
        var bounds = SelectedBounds();
        if (!bounds.IsEmpty)
        {
            _camera = ObserverCamera.Fit(bounds, ViewportSize(), 72);
            NotifyViewChanged();
        }
    }

    internal void ResetView()
    {
        _camera = ObserverCamera.Default;
        NotifyViewChanged();
    }

    internal void SetCamera(ObserverCamera camera)
    {
        _camera = camera ?? ObserverCamera.Default;
        NotifyViewChanged();
    }

    internal void Zoom(double factor)
    {
        var viewport = ViewportSize();
        _camera = _camera.ZoomAt(
            new ObserverPoint(viewport.Width / 2, viewport.Height / 2),
            factor,
            viewport);
        NotifyViewChanged();
    }

    internal IReadOnlyList<ObserverSheetCard> VisibleSheets(bool includeOverscan)
    {
        var visible = _camera.VisibleWorld(ViewportSize());
        var overscan = includeOverscan ? 160 / _camera.Zoom : 0;
        return _spatialIndex.QuerySheets(visible.Inflate(overscan))
            .Where(card => IsPreviewEligible(card.Sheet.PageViewId))
            .ToArray();
    }

    internal bool IsPreviewEligible(Guid sheetId) =>
        _presentation.TierForSheet(sheetId) == ObserverCanvasLodTier.Detail;

    internal int CurrentPreviewBucket(Guid sheetId) =>
        _previews.TryGetValue(sheetId, out var preview) ? preview.Key.ResolutionBucket : 0;

    internal bool HasCurrentPreview(Guid sheetId, long contentVersion, int minimumBucket) =>
        _previews.TryGetValue(sheetId, out var preview) &&
        preview.Key.ContentVersion == contentVersion &&
        preview.Key.ResolutionBucket >= minimumBucket;

    internal void SetPreview(OverviewThumbnailKey key, Bitmap bitmap)
    {
        if (_previews.Remove(key.SheetPageViewId, out var previous) &&
            !ReferenceEquals(previous.Bitmap, bitmap))
        {
            previous.Bitmap.Dispose();
        }

        _previews[key.SheetPageViewId] = new PreviewEntry(key, bitmap);
        Invalidate();
    }

    internal void InvalidatePreviews(IReadOnlySet<Guid>? sheetIds = null)
    {
        foreach (var pair in _previews
                     .Where(pair => sheetIds is null || sheetIds.Count == 0 || sheetIds.Contains(pair.Key))
                     .ToArray())
        {
            pair.Value.Bitmap.Dispose();
            _previews.Remove(pair.Key);
        }

        Invalidate();
    }

    internal void PrunePreviews(IReadOnlySet<Guid> retainedSheetIds)
    {
        foreach (var pair in _previews.Where(pair => !retainedSheetIds.Contains(pair.Key)).ToArray())
        {
            pair.Value.Bitmap.Dispose();
            _previews.Remove(pair.Key);
        }
    }

    internal void ReleasePreviews()
    {
        foreach (var preview in _previews.Values) preview.Bitmap.Dispose();
        _previews.Clear();
        InvalidateNamedViewPreviews();
    }

    private void OnPaint(object? sender, PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.AntiAlias = true;
        graphics.FillRectangle(FoundryTheme.CanvasBackground, eventArgs.ClipRectangle);
        if (!_snapshot.HasDocument)
        {
            DrawEmpty(graphics, "Open a Rhino document to use the observer canvas.");
            return;
        }

        if (_snapshot.Sheets.Count == 0)
        {
            DrawEmpty(graphics, "Create a layout to begin arranging the board.");
            DrawNavigator(graphics, ViewportSize());
            DrawNamedViews(graphics, ViewportSize());
            return;
        }

        var viewport = ViewportSize();
        var visibleWorld = _camera.VisibleWorld(viewport).Inflate(160 / _camera.Zoom);
        DrawGrid(graphics, viewport);
        foreach (var frame in _spatialIndex.QueryFolders(visibleWorld))
        {
            if (FolderHasDrawableDescendant(frame.Folder.Id))
                DrawFolder(graphics, frame, viewport);
        }

        foreach (var card in _spatialIndex.QuerySheets(visibleWorld))
        {
            var tier = _presentation.TierForSheet(card.Sheet.PageViewId);
            if (tier != ObserverCanvasLodTier.Folder)
                DrawSheet(graphics, card, viewport, tier, eventArgs.ClipRectangle);
        }

        foreach (var summary in _presentation.FolderSummaries)
            DrawFolderSummary(graphics, summary, viewport);

        if (_lassoWorld is { } lasso)
        {
            var screen = ScreenRect(lasso, viewport);
            var crossing = lasso.Width < 0;
            graphics.FillRectangle(FoundryTheme.SelectionWindowFill(crossing), screen);
            graphics.DrawRectangle(new Pen(FoundryTheme.SelectionWindowStroke(crossing), 1), screen);
        }

        DrawNavigator(graphics, viewport);
        DrawNamedViews(graphics, viewport);
    }

    private void DrawGrid(Graphics graphics, ObserverSize viewport)
    {
        if (_camera.Zoom < 0.18 || _gridOpacity <= 0) return;
        var spacing = ObserverCanvasGridPolicy.EffectiveWorldSpacing(_camera.Zoom);
        var visible = _camera.VisibleWorld(viewport);
        var startX = Math.Floor(visible.Left / spacing) * spacing;
        var startY = Math.Floor(visible.Top / spacing) * spacing;
        var color = FoundryTheme.WithAlpha(_gridColor, (int)Math.Round(_gridOpacity * 255));
        const float dotSize = 1.5f;
        const float dotRadius = dotSize / 2;
        for (var x = startX; x <= visible.Right; x += spacing)
        for (var y = startY; y <= visible.Bottom; y += spacing)
        {
            var point = _camera.WorldToScreen(new ObserverPoint(x, y), viewport);
            graphics.FillEllipse(
                color,
                (float)point.X - dotRadius,
                (float)point.Y - dotRadius,
                dotSize,
                dotSize);
        }
    }

    private void DrawFolder(Graphics graphics, ObserverFolderFrame frame, ObserverSize viewport)
    {
        var bounds = ScreenRect(PreviewBounds(frame.Bounds, frame.Folder.Id), viewport);
        if (bounds.Width < 8 || bounds.Height < 8) return;
        var key = new OverviewNodeKey(OverviewNodeKind.Folder, frame.Folder.Id);
        var selected = _selection.Contains(key);
        var emphasized = _filter.Emphasizes(key);
        var outline = selected
            ? FoundryTheme.SelectionAccent
            : emphasized
                ? FoundryTheme.CanvasBorder
                : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 64);
        graphics.FillRectangle(
            FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, emphasized ? 118 : 30),
            bounds);
        graphics.DrawRectangle(new Pen(outline, selected ? 2 : 1), bounds);
        var headerHeight = Math.Max(22, ObserverPlacementPlanner.FolderHeaderHeight * _camera.Zoom);
        graphics.FillRectangle(
            emphasized
                ? FoundryTheme.CanvasFolderBackground
                : FoundryTheme.WithAlpha(FoundryTheme.CanvasFolderBackground, 55),
            bounds.X, bounds.Y, bounds.Width, (float)Math.Min(bounds.Height, headerHeight));
        var headerColor = emphasized
            ? FoundryTheme.PrimaryText
            : FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 64);
        FoundryHierarchyIcons.DrawFolder(
            graphics,
            headerColor,
            new RectangleF(bounds.X + 10, bounds.Y + 5, 14, 14));
        graphics.DrawText(
            _folderFont,
            headerColor,
            bounds.X + 30,
            bounds.Y + 6,
            FitText(
                graphics,
                _folderFont,
                $"{frame.Folder.Name}   ·   {frame.DirectSheetCount} layout{(frame.DirectSheetCount == 1 ? string.Empty : "s")}",
                Math.Max(8, bounds.Width - 38)));
    }

    private void DrawSheet(
        Graphics graphics,
        ObserverSheetCard card,
        ObserverSize viewport,
        ObserverCanvasLodTier tier,
        RectangleF clipRectangle)
    {
        var worldBounds = PreviewBounds(card.Bounds, card.Sheet.PageViewId);
        var bounds = ScreenRect(worldBounds, viewport);
        if (bounds.Width < 3 || bounds.Height < 3) return;
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId);
        var selected = _selection.Contains(key);
        var emphasized = _filter.Emphasizes(key);
        var hasSelectedDetail = card.Sheet.Details.Any(detail =>
            _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId)));
        if (tier == ObserverCanvasLodTier.Sheet)
        {
            DrawSheetSummary(graphics, card, bounds, selected, hasSelectedDetail, emphasized);
            return;
        }

        graphics.FillRectangle(Color.FromArgb(0, 0, 0, emphasized ? 45 : 12),
            bounds.X + 4, bounds.Y + 5, bounds.Width, bounds.Height);
        graphics.FillRectangle(Colors.White, bounds);
        if (_previews.TryGetValue(card.Sheet.PageViewId, out var preview) &&
            preview.Key.ContentVersion == card.Sheet.PreviewContentVersion)
        {
            DrawVisibleImage(graphics, preview.Bitmap, bounds, clipRectangle);
        }
        else
        {
            DrawPlaceholder(graphics, card, bounds);
        }

        if (!emphasized)
        {
            graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasBackground, 190), bounds);
        }

        var border = selected || hasSelectedDetail
            ? FoundryTheme.SelectionAccent
            : emphasized
                ? FoundryTheme.CanvasBorder
                : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 64);
        graphics.DrawRectangle(new Pen(border, selected ? 3 : hasSelectedDetail ? 2 : 1), bounds);
        if (bounds.Width >= 70 && bounds.Height >= 50)
        {
            DrawDetailOverlays(graphics, card, bounds, selected);
            graphics.FillRectangle(
                card.Sheet.IncludeInPrintAll
                    ? emphasized
                        ? FoundryTheme.PrimaryText
                        : FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 64)
                    : emphasized
                        ? FoundryTheme.MutedText
                        : FoundryTheme.WithAlpha(FoundryTheme.MutedText, 64),
                bounds.Right - 18,
                bounds.Top + 6,
                10,
                10);
            graphics.DrawRectangle(FoundryTheme.CanvasBorder,
                bounds.Right - 18, bounds.Top + 6, 10, 10);
            graphics.FillRectangle(FoundryTheme.WithAlpha(
                    FoundryTheme.CanvasSubtleSurface,
                    emphasized ? 230 : 58),
                bounds.Left + 6, bounds.Top + 6, 12, 12);
            graphics.DrawText(
                _smallFont,
                emphasized ? Colors.White : FoundryTheme.WithAlpha(Colors.White, 64),
                bounds.Left + 8,
                bounds.Top + 5,
                "↕");
        }

        var useInternalLabel = ObserverPlacementPlanner.SheetGap * _camera.Zoom < 18;
        if (useInternalLabel)
        {
            DrawSheetNameScrim(graphics, bounds, card.Sheet.Name, emphasized);
        }
        else
        {
            var labelColor = emphasized
                ? FoundryTheme.PrimaryText
                : FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 64);
            FoundryHierarchyIcons.DrawLayout(
                graphics,
                labelColor,
                new RectangleF(bounds.Left, bounds.Bottom + 4, 14, 14));
            graphics.DrawText(
                _sheetFont,
                labelColor,
                bounds.Left + 20,
                bounds.Bottom + 5,
                FitText(graphics, _sheetFont, card.Sheet.Name, Math.Max(8, bounds.Width - 20)));
        }
        if (selected && _dragMode == DragMode.Sheets && _dragWorldDelta != new ObserverPoint())
        {
            graphics.DrawRectangle(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.SelectionAccent, 180), 1),
                bounds);
        }
    }

    private static void DrawVisibleImage(
        Graphics graphics,
        Image image,
        RectangleF destination,
        RectangleF clipRectangle)
    {
        var left = Math.Max(destination.Left, clipRectangle.Left);
        var top = Math.Max(destination.Top, clipRectangle.Top);
        var right = Math.Min(destination.Right, clipRectangle.Right);
        var bottom = Math.Min(destination.Bottom, clipRectangle.Bottom);
        if (right <= left || bottom <= top || destination.Width <= 0 || destination.Height <= 0)
            return;

        var visibleDestination = new RectangleF(left, top, right - left, bottom - top);
        var source = new RectangleF(
            (left - destination.Left) / destination.Width * image.Width,
            (top - destination.Top) / destination.Height * image.Height,
            visibleDestination.Width / destination.Width * image.Width,
            visibleDestination.Height / destination.Height * image.Height);
        graphics.DrawImage(image, source, visibleDestination);
    }

    private void DrawSheetSummary(
        Graphics graphics,
        ObserverSheetCard card,
        RectangleF bounds,
        bool selected,
        bool hasSelectedDetail,
        bool emphasized)
    {
        graphics.FillRectangle(
            FoundryTheme.WithAlpha(Colors.Black, emphasized ? 45 : 18),
            bounds.X + 3,
            bounds.Y + 4,
            bounds.Width,
            bounds.Height);
        graphics.FillRectangle(
            emphasized ? Color.FromArgb(245, 245, 245, 255) : Color.FromArgb(226, 226, 226, 255),
            bounds);
        var border = selected || hasSelectedDetail
            ? FoundryTheme.SelectionAccent
            : emphasized
                ? FoundryTheme.CanvasBorder
                : FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 90);
        graphics.DrawRectangle(new Pen(border, selected ? 3 : hasSelectedDetail ? 2 : 1), bounds);
        DrawSheetNameScrim(graphics, bounds, card.Sheet.Name, emphasized);

        var selectedDetailCount = card.Sheet.Details.Count(detail =>
            _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId)));
        if (selectedDetailCount > 0)
            DrawSelectionBadge(graphics, bounds.Right - 8, bounds.Top + 8, selectedDetailCount);
    }

    private void DrawSheetNameScrim(
        Graphics graphics,
        RectangleF bounds,
        string name,
        bool emphasized)
    {
        if (bounds.Width < 16 || bounds.Height < 12) return;
        var maximumWidth = Math.Max(8, bounds.Width - 10);
        var fitted = FitText(graphics, _sheetFont, name, maximumWidth - 10);
        var measured = graphics.MeasureString(_sheetFont, fitted);
        var scrimWidth = Math.Min(maximumWidth, Math.Max(30, measured.Width + 10));
        var scrimHeight = Math.Min(22, bounds.Height);
        var scrim = new RectangleF(
            bounds.Left + 5,
            bounds.Bottom - scrimHeight - 5,
            scrimWidth,
            scrimHeight);
        if (scrim.Top < bounds.Top) scrim.Y = bounds.Top;
        graphics.FillRectangle(
            FoundryTheme.WithAlpha(FoundryTheme.CanvasOverlayBackground, emphasized ? 235 : 205),
            scrim);
        graphics.DrawText(
            _sheetFont,
            emphasized ? FoundryTheme.PrimaryText : FoundryTheme.SecondaryText,
            scrim.Left + 5,
            scrim.Top + Math.Max(1, (scrim.Height - measured.Height) / 2),
            fitted);
    }

    private void DrawFolderSummary(
        Graphics graphics,
        ObserverFolderSummary summary,
        ObserverSize viewport)
    {
        var bounds = Rect(summary.ScreenBounds);
        if (_dragMode == DragMode.Folder && _dragFolderId is { } draggedFolderId &&
            IsFolderDescendant(summary.FolderId, draggedFolderId))
        {
            bounds.X += (float)(_dragWorldDelta.X * _camera.Zoom);
            bounds.Y += (float)(_dragWorldDelta.Y * _camera.Zoom);
        }
        if (bounds.Right < 0 || bounds.Bottom < 0 ||
            bounds.Left > viewport.Width || bounds.Top > viewport.Height) return;

        var selectionCount = SummarySelectionCount(summary);
        var selected = selectionCount > 0;
        graphics.FillRectangle(
            FoundryTheme.WithAlpha(Colors.Black, 44),
            bounds.X + 3,
            bounds.Y + 4,
            bounds.Width,
            bounds.Height);
        graphics.FillRectangle(FoundryTheme.CanvasOverlayBackground, bounds);
        graphics.DrawRectangle(
            new Pen(selected ? FoundryTheme.SelectionAccent : FoundryTheme.CanvasBorder, selected ? 2 : 1),
            bounds);

        var iconColor = selected ? FoundryTheme.PrimaryText : FoundryTheme.SecondaryText;
        FoundryHierarchyIcons.DrawFolder(
            graphics,
            iconColor,
            new RectangleF(bounds.Left + 8, bounds.Top + 8, 14, 14));
        var countLabel = $"{summary.LayoutCount} layout{(summary.LayoutCount == 1 ? string.Empty : "s")}";
        var badgeWidth = selected ? 30 : 0;
        var text = $"{summary.Name}  ·  {countLabel}";
        graphics.DrawText(
            _folderFont,
            FoundryTheme.PrimaryText,
            bounds.Left + 28,
            bounds.Top + 7,
            FitText(graphics, _folderFont, text, Math.Max(8, bounds.Width - 38 - badgeWidth)));
        if (selected)
            DrawSelectionBadge(graphics, bounds.Right - 12, bounds.Top + bounds.Height / 2, selectionCount);
    }

    private void DrawSelectionBadge(Graphics graphics, float centerX, float centerY, int count)
    {
        var label = count > 99 ? "99+" : count.ToString();
        var diameter = count > 9 ? 22f : 18f;
        graphics.FillEllipse(
            FoundryTheme.SelectionAccent,
            centerX - diameter / 2,
            centerY - diameter / 2,
            diameter,
            diameter);
        var measured = graphics.MeasureString(_smallFont, label);
        graphics.DrawText(
            _smallFont,
            Colors.White,
            centerX - measured.Width / 2,
            centerY - measured.Height / 2,
            label);
    }

    private int SummarySelectionCount(ObserverFolderSummary summary)
    {
        var represented = summary.RepresentedSheetIds;
        return _selection.Count(key => key.Kind switch
        {
            OverviewNodeKind.Folder => IsFolderDescendant(key.Id, summary.FolderId),
            OverviewNodeKind.Sheet => represented.Contains(key.Id),
            OverviewNodeKind.Detail => _snapshot.Sheets.Any(sheet =>
                represented.Contains(sheet.PageViewId) &&
                sheet.Details.Any(detail => detail.DetailViewportId == key.Id)),
            _ => false,
        });
    }

    private bool FolderHasDrawableDescendant(Guid folderId) => _drawableFolderIds.Contains(folderId);

    private static string FitText(Graphics graphics, Font font, string text, float maximumWidth)
    {
        if (maximumWidth <= 0) return string.Empty;
        if (graphics.MeasureString(font, text).Width <= maximumWidth) return text;
        const string ellipsis = "…";
        for (var length = text.Length - 1; length > 0; length--)
        {
            var candidate = text[..length].TrimEnd() + ellipsis;
            if (graphics.MeasureString(font, candidate).Width <= maximumWidth) return candidate;
        }
        return ellipsis;
    }

    private static RectangleF Rect(ObserverRect bounds) => new(
        (float)bounds.Left,
        (float)bounds.Top,
        (float)(bounds.Right - bounds.Left),
        (float)(bounds.Bottom - bounds.Top));

    private static void DrawPlaceholder(Graphics graphics, ObserverSheetCard card, RectangleF bounds)
    {
        graphics.FillRectangle(Color.FromArgb(236, 236, 232, 255), bounds);
        foreach (var detail in card.Sheet.Details)
        {
            var rect = DetailScreenRect(detail.NormalizedBounds, bounds);
            graphics.FillRectangle(Color.FromArgb(218, 220, 222, 255), rect);
            graphics.DrawRectangle(Color.FromArgb(105, 108, 110, 160), rect);
        }

        graphics.DrawLine(Color.FromArgb(110, 110, 110, 100),
            bounds.Left + 8, bounds.Bottom - 12, bounds.Right - 8, bounds.Bottom - 12);
    }

    private void DrawDetailOverlays(
        Graphics graphics,
        ObserverSheetCard card,
        RectangleF bounds,
        bool sheetSelected)
    {
        foreach (var detail in card.Sheet.Details)
        {
            var detailSelected = _selection.Contains(
                new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId));
            var hovered = _hoverDetailId == detail.DetailViewportId;
            var rect = DetailScreenRect(detail.NormalizedBounds, bounds);
            if (rect.Width < 5 || rect.Height < 5) continue;
            var prominent = sheetSelected || detailSelected || hovered;
            graphics.DrawRectangle(
                new Pen(
                    FoundryTheme.WithAlpha(
                        prominent ? FoundryTheme.SelectionAccent : FoundryTheme.CanvasBorder,
                        detailSelected ? 255 : hovered ? 220 : sheetSelected ? 155 : 90),
                    detailSelected ? 3 : hovered ? 2 : 1),
                rect);
            if (detailSelected || hovered)
            {
                graphics.FillRectangle(
                    FoundryTheme.WithAlpha(FoundryTheme.CanvasBackground, 205),
                    rect.Left,
                    rect.Top,
                    Math.Min(rect.Width, Math.Max(54, detail.Name.Length * 6 + 10)),
                    18);
                DrawOverlayText(
                    graphics,
                    _smallFont,
                    FoundryTheme.PrimaryText,
                    rect.Left + 4,
                    rect.Top + 3,
                    detail.Name);
            }
        }
    }

    private void DrawEmpty(Graphics graphics, string message)
    {
        var x = _navigatorVisible ? NavigatorWidth + 32 : 32;
        graphics.DrawText(
            SystemFonts.Bold(13),
            FoundryTheme.PrimaryText,
            x,
            44,
            "Observer canvas");
        graphics.DrawText(SystemFonts.Default(), FoundryTheme.MutedText, x, 72, message);
    }

    private static void DrawOverlayText(
        Graphics graphics,
        Font font,
        Color color,
        float x,
        float y,
        string text)
    {
        var halo = FoundryTheme.WithAlpha(Colors.Black, 185);
        graphics.DrawText(font, halo, x - 1, y, text);
        graphics.DrawText(font, halo, x + 1, y, text);
        graphics.DrawText(font, halo, x, y - 1, text);
        graphics.DrawText(font, halo, x, y + 1, text);
        graphics.DrawText(font, color, x, y, text);
    }

    private void DrawNavigator(Graphics graphics, ObserverSize viewport)
    {
        if (!_navigatorVisible || !_snapshot.HasDocument) return;
        var rows = NavigatorRowsForDisplay();
        var visibleCount = NavigatorVisibleRowCount(viewport);
        ClampNavigatorScroll(rows.Length, visibleCount);

        for (var visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            var rowIndex = _navigatorScrollRow + visibleIndex;
            if (rowIndex >= rows.Length) break;
            var row = rows[rowIndex];
            var y = NavigatorTop + NavigatorHeaderHeight + visibleIndex * NavigatorRowHeight;
            var selected = _selection.Contains(row.Key);
            var destinationHighlighted = _dragMode == DragMode.Navigator &&
                                         _navigatorDrop is { IsValid: true, HighlightFolderId: { } folderId } &&
                                         row.Key.Kind == OverviewNodeKind.Folder && row.Key.Id == folderId;
            if (destinationHighlighted)
            {
                graphics.FillRectangle(
                    FoundryTheme.WithAlpha(FoundryTheme.SelectionAccent, 52),
                    0,
                    y,
                    NavigatorWidth,
                    NavigatorRowHeight);
                graphics.DrawRectangle(
                    new Pen(FoundryTheme.SelectionAccent, 1),
                    1,
                    y + 1,
                    NavigatorWidth - 3,
                    NavigatorRowHeight - 2);
            }
            if (selected || row.IsDraft)
            {
                graphics.FillRectangle(
                    row.IsDraft
                        ? FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 135)
                        : FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 190),
                    0,
                    y,
                    NavigatorWidth,
                    NavigatorRowHeight);
            }

            var emphasized = row.IsDraft || !_filter.IsActive || _filter.Emphasizes(row.Key) || selected;
            var disclosureX = 8 + row.Depth * 16;
            DrawNavigatorConnectors(graphics, row, y, disclosureX);
            if (row.CanExpand)
            {
                DrawOverlayText(
                    graphics,
                    _sheetFont,
                    emphasized ? FoundryTheme.PrimaryText : FoundryTheme.WithAlpha(FoundryTheme.MutedText, 80),
                    disclosureX,
                    y + 5,
                    row.IsExpanded ? "▾" : "▸");
            }
            var rowColor = emphasized
                ? FoundryTheme.PrimaryText
                : FoundryTheme.WithAlpha(FoundryTheme.MutedText, 80);
            DrawNavigatorIcon(graphics, row, rowColor, disclosureX + 14, y + 3);
            DrawOverlayText(
                graphics,
                _sheetFont,
                rowColor,
                disclosureX + 36,
                y + 5,
                row.Label);
            if (row.IsDraft)
            {
                graphics.DrawRectangle(
                    new Pen(FoundryTheme.SelectionAccent, 1),
                    4,
                    y + 2,
                    NavigatorWidth - 8,
                    NavigatorRowHeight - 4);
            }
        }

        if (_dragMode == DragMode.Navigator &&
            _navigatorDrop is { IsValid: true, InsertionLineY: { } insertionY })
        {
            graphics.DrawLine(
                new Pen(FoundryTheme.SelectionAccent, 2),
                4,
                (float)insertionY,
                NavigatorWidth - 5,
                (float)insertionY);
        }

        if (rows.Length > visibleCount)
        {
            var trackHeight = visibleCount * NavigatorRowHeight;
            var thumbHeight = Math.Max(18, trackHeight * visibleCount / rows.Length);
            var maxScroll = Math.Max(1, rows.Length - visibleCount);
            var thumbY = NavigatorTop + NavigatorHeaderHeight +
                         (trackHeight - thumbHeight) * _navigatorScrollRow / maxScroll;
            graphics.FillRectangle(
                FoundryTheme.WithAlpha(FoundryTheme.MutedText, 90),
                NavigatorWidth - 3,
                thumbY,
                2,
                thumbHeight);
        }
    }

    private static void DrawNavigatorIcon(
        Graphics graphics,
        CanvasNavigatorRow row,
        Color iconColor,
        float x,
        float y)
    {
        var badgeBounds = new RectangleF(x, y, 18, 18);
        using (var badge = GraphicsPath.GetRoundRect(badgeBounds, 3))
        {
            graphics.FillPath(FoundryTheme.CanvasOverlayBackground, badge);
            graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 190), 1),
                badge);
        }

        var iconBounds = new RectangleF(x + 2, y + 2, 14, 14);
        if (row.IsDocumentRoot)
        {
            FoundryHierarchyIcons.DrawRhino(graphics, iconColor, iconBounds);
            return;
        }

        switch (row.Key.Kind)
        {
            case OverviewNodeKind.Folder:
                FoundryHierarchyIcons.DrawFolder(graphics, iconColor, iconBounds);
                break;
            case OverviewNodeKind.Sheet:
                FoundryHierarchyIcons.DrawLayout(graphics, iconColor, iconBounds);
                break;
            case OverviewNodeKind.Detail:
                FoundryHierarchyIcons.DrawDetail(graphics, iconColor, iconBounds);
                break;
        }
    }

    private static void DrawNavigatorConnectors(
        Graphics graphics,
        CanvasNavigatorRow row,
        float rowTop,
        float disclosureX)
    {
        if (row.Depth <= 0) return;
        var rowMiddle = rowTop + NavigatorRowHeight / 2f;
        var continuations = row.AncestorContinuations ?? [];
        for (var index = 0; index < continuations.Count; index++)
        {
            if (!continuations[index]) continue;
            var x = 16 + index * 16;
            DrawNavigatorConnectorLine(graphics, x, rowTop, x, rowTop + NavigatorRowHeight);
        }

        var branchX = row.Depth * 16;
        DrawNavigatorConnectorLine(
            graphics,
            branchX,
            rowTop,
            branchX,
            row.HasNextSibling ? rowTop + NavigatorRowHeight : rowMiddle);
        DrawNavigatorConnectorLine(
            graphics,
            branchX,
            rowMiddle,
            row.CanExpand ? disclosureX - 2 : disclosureX + 13,
            rowMiddle);
    }

    private static void DrawNavigatorConnectorLine(
        Graphics graphics,
        float x1,
        float y1,
        float x2,
        float y2)
    {
        using var halo = new Pen(FoundryTheme.WithAlpha(Colors.Black, 190), 3);
        using var line = new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 145), 1);
        graphics.DrawLine(halo, x1, y1, x2, y2);
        graphics.DrawLine(line, x1, y1, x2, y2);
    }

    private void DrawNamedViews(Graphics graphics, ObserverSize viewport)
    {
        if (!_namedViewsVisible || !_snapshot.HasDocument) return;
        var left = NamedViewsLeft(viewport);
        var capacity = NamedViewsVisibleItemCount(viewport);
        ClampNamedViewsScroll(capacity);

        DrawOverlayText(
            graphics,
            _folderFont,
            FoundryTheme.PrimaryText,
            left + 8,
            NamedViewsTop + 5,
            "Named views");
        DrawNamedViewsModeToggles(graphics, left);

        var visibleNames = _snapshot.NamedViews
            .Skip(_namedViewsScrollRow)
            .Take(capacity)
            .ToArray();
        if (_namedViewsThumbnailMode)
            DrawNamedViewThumbnails(graphics, left, visibleNames);
        else
            DrawNamedViewList(graphics, left, visibleNames);

        if (visibleNames.Length == 0)
        {
            DrawOverlayText(
                graphics,
                SystemFonts.Default(),
                FoundryTheme.MutedText,
                left + 8,
                NamedViewsTop + NamedViewsHeaderHeight + 5,
                "No named views");
        }

        if (_snapshot.NamedViews.Count > capacity)
        {
            var trackHeight = NamedViewsContentHeight(capacity);
            var thumbHeight = Math.Max(18, trackHeight * capacity / _snapshot.NamedViews.Count);
            var maxScroll = Math.Max(1, _snapshot.NamedViews.Count - capacity);
            var thumbY = NamedViewsTop + NamedViewsHeaderHeight +
                         (trackHeight - thumbHeight) * _namedViewsScrollRow / maxScroll;
            graphics.FillRectangle(
                FoundryTheme.WithAlpha(FoundryTheme.MutedText, 90),
                left + NamedViewsWidth - 3,
                thumbY,
                2,
                thumbHeight);
        }

        var action = NamedViewsActionBounds(viewport, visibleNames.Length);
        var actionColor = _selectedNamedView is null
            ? FoundryTheme.WithAlpha(FoundryTheme.MutedText, 80)
            : FoundryTheme.PrimaryText;
        graphics.DrawRectangle(
            new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 100), 1),
            action);
        DrawOverlayText(
            graphics,
            _sheetFont,
            actionColor,
            action.Left + 9,
            action.Top + 7,
            "Assign to selection");
    }

    private void DrawNamedViewsModeToggles(Graphics graphics, float left)
    {
        var list = NamedViewsListToggleBounds(left);
        var thumbnails = NamedViewsThumbnailToggleBounds(left);
        var group = new RectangleF(
            list.Left - 3,
            list.Top - 3,
            thumbnails.Right - list.Left + 6,
            Math.Max(list.Height, thumbnails.Height) + 6);
        using (var groupPath = GraphicsPath.GetRoundRect(group, 7))
        {
            graphics.FillPath(FoundryTheme.ToolbarGroupBackground, groupPath);
            graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.CanvasBorder, 75), 1),
                groupPath);
        }
        var active = _namedViewsThumbnailMode ? thumbnails : list;
        using (var activePath = GraphicsPath.GetRoundRect(active, 5))
        {
            graphics.FillPath(FoundryTheme.ToolbarActiveBackground, activePath);
            graphics.DrawPath(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.SecondaryText, 205), 1),
                activePath);
        }
        var listColor = _namedViewsThumbnailMode ? FoundryTheme.MutedText : FoundryTheme.PrimaryText;
        var thumbnailColor = _namedViewsThumbnailMode ? FoundryTheme.PrimaryText : FoundryTheme.MutedText;
        using var listPen = new Pen(listColor, 1);
        for (var index = 0; index < 3; index++)
            graphics.DrawLine(listPen, list.Left + 4, list.Top + 5 + index * 5,
                list.Right - 4, list.Top + 5 + index * 5);
        using var thumbnailPen = new Pen(thumbnailColor, 1);
        for (var row = 0; row < 2; row++)
        for (var column = 0; column < 2; column++)
            graphics.DrawRectangle(thumbnailPen,
                thumbnails.Left + 4 + column * 7,
                thumbnails.Top + 4 + row * 7,
                5,
                5);
    }

    private void DrawNamedViewList(Graphics graphics, float left, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var y = NamedViewsTop + NamedViewsHeaderHeight + index * NamedViewsRowHeight;
            if (string.Equals(name, _selectedNamedView, StringComparison.Ordinal))
                graphics.FillRectangle(
                    FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, 190),
                    left, y, NamedViewsWidth, NamedViewsRowHeight);
            DrawOverlayText(graphics, _sheetFont, FoundryTheme.PrimaryText, left + 8, y + 5, name);
        }
    }

    private void DrawNamedViewThumbnails(Graphics graphics, float left, IReadOnlyList<string> names)
    {
        const float gutter = 6;
        var cardWidth = (NamedViewsWidth - gutter * 3) / NamedViewsThumbnailColumns;
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var column = index % NamedViewsThumbnailColumns;
            var row = index / NamedViewsThumbnailColumns;
            var card = new RectangleF(
                left + gutter + column * (cardWidth + gutter),
                NamedViewsTop + NamedViewsHeaderHeight + row * NamedViewsThumbnailCardHeight,
                cardWidth,
                NamedViewsThumbnailCardHeight - gutter);
            var selected = string.Equals(name, _selectedNamedView, StringComparison.Ordinal);
            graphics.FillRectangle(
                FoundryTheme.WithAlpha(FoundryTheme.CanvasSubtleSurface, selected ? 205 : 145),
                card);
            var image = new RectangleF(card.Left + 4, card.Top + 4, card.Width - 8, 60);
            if (_namedViewPreviews.TryGetValue(name, out var preview))
            {
                graphics.DrawImage(preview.Bitmap, image);
            }
            else
            {
                graphics.FillRectangle(FoundryTheme.WithAlpha(FoundryTheme.CanvasSurface, 225), image);
                using var placeholderPen = new Pen(FoundryTheme.WithAlpha(FoundryTheme.MutedText, 90), 1);
                graphics.DrawLine(placeholderPen, image.Left + 8, image.Bottom - 10,
                    image.Left + image.Width * 0.45f, image.Top + image.Height * 0.48f);
                graphics.DrawLine(placeholderPen, image.Left + image.Width * 0.45f,
                    image.Top + image.Height * 0.48f, image.Right - 8, image.Bottom - 15);
            }

            if (selected)
                graphics.DrawRectangle(new Pen(FoundryTheme.SelectionAccent, 2), card);
            DrawOverlayText(graphics, _smallFont, FoundryTheme.PrimaryText,
                card.Left + 4, card.Bottom - 17, CompactNamedViewLabel(name));
        }
    }

    private static string CompactNamedViewLabel(string name) =>
        name.Length <= 14 ? name : $"{name[..13]}…";

    private float NamedViewsLeft(ObserverSize viewport) =>
        (float)Math.Max(0, viewport.Width - NamedViewsWidth);

    private int NamedViewsVisibleItemCount(ObserverSize viewport)
    {
        var available = viewport.Height - NamedViewsTop - NamedViewsHeaderHeight -
                        NamedViewsActionHeight - 16;
        return _namedViewsThumbnailMode
            ? Math.Max(NamedViewsThumbnailColumns,
                (int)Math.Floor(available / NamedViewsThumbnailCardHeight) * NamedViewsThumbnailColumns)
            : Math.Max(1, (int)Math.Floor(available / NamedViewsRowHeight));
    }

    private int NamedViewsContentHeight(int itemCount) => _namedViewsThumbnailMode
        ? Math.Max(1, (int)Math.Ceiling(itemCount / (double)NamedViewsThumbnailColumns)) *
          NamedViewsThumbnailCardHeight
        : Math.Max(1, itemCount) * NamedViewsRowHeight;

    private RectangleF NamedViewsActionBounds(ObserverSize viewport, int visibleNameCount)
    {
        var top = NamedViewsTop + NamedViewsHeaderHeight +
                  NamedViewsContentHeight(visibleNameCount) + 6;
        return new RectangleF(NamedViewsLeft(viewport) + 4, top, 150, NamedViewsActionHeight);
    }

    private RectangleF NamedViewsListToggleBounds(float left) =>
        new(left + NamedViewsWidth - 50, NamedViewsTop + 2, 22, 23);

    private RectangleF NamedViewsThumbnailToggleBounds(float left) =>
        new(left + NamedViewsWidth - 25, NamedViewsTop + 2, 22, 23);

    private bool TryNamedViewsModeToggleAt(PointF point, out bool thumbnails)
    {
        thumbnails = false;
        if (!_namedViewsVisible) return false;
        var left = NamedViewsLeft(ViewportSize());
        if (NamedViewsListToggleBounds(left).Contains(point)) return true;
        if (!NamedViewsThumbnailToggleBounds(left).Contains(point)) return false;
        thumbnails = true;
        return true;
    }

    private void ClampNamedViewsScroll() => ClampNamedViewsScroll(NamedViewsVisibleItemCount(ViewportSize()));

    private void ClampNamedViewsScroll(int capacity)
    {
        var maximum = Math.Max(0, _snapshot.NamedViews.Count - capacity);
        if (_namedViewsThumbnailMode && maximum > 0)
            maximum = (int)Math.Ceiling(maximum / (double)NamedViewsThumbnailColumns) *
                      NamedViewsThumbnailColumns;
        _namedViewsScrollRow = Math.Clamp(
            _namedViewsScrollRow,
            0,
            maximum);
    }

    private bool TryNamedViewAt(PointF point, out string name)
    {
        name = string.Empty;
        if (!_namedViewsVisible) return false;
        var viewport = ViewportSize();
        var left = NamedViewsLeft(viewport);
        if (point.X < left || point.X > left + NamedViewsWidth ||
            point.Y < NamedViewsTop + NamedViewsHeaderHeight)
            return false;
        var capacity = NamedViewsVisibleItemCount(viewport);
        int visibleIndex;
        if (_namedViewsThumbnailMode)
        {
            const float gutter = 6;
            var cardWidth = (NamedViewsWidth - gutter * 3) / NamedViewsThumbnailColumns;
            var localX = point.X - left - gutter;
            var localY = point.Y - NamedViewsTop - NamedViewsHeaderHeight;
            if (localX < 0 || localY < 0) return false;
            var column = (int)(localX / (cardWidth + gutter));
            var row = (int)(localY / NamedViewsThumbnailCardHeight);
            if (column < 0 || column >= NamedViewsThumbnailColumns ||
                localX - column * (cardWidth + gutter) > cardWidth)
                return false;
            visibleIndex = row * NamedViewsThumbnailColumns + column;
        }
        else
        {
            visibleIndex = (int)((point.Y - NamedViewsTop - NamedViewsHeaderHeight) /
                                 NamedViewsRowHeight);
        }

        if (visibleIndex < 0 || visibleIndex >= capacity) return false;
        var index = _namedViewsScrollRow + visibleIndex;
        if (index < 0 || index >= _snapshot.NamedViews.Count) return false;
        name = _snapshot.NamedViews[index];
        return true;
    }

    private bool IsNamedViewsActionAt(PointF point)
    {
        if (!_namedViewsVisible) return false;
        var visibleCount = Math.Min(
            NamedViewsVisibleItemCount(ViewportSize()),
            Math.Max(0, _snapshot.NamedViews.Count - _namedViewsScrollRow));
        return NamedViewsActionBounds(ViewportSize(), visibleCount).Contains(point);
    }

    private CanvasNavigatorRow[] NavigatorRowsForDisplay()
    {
        if (_navigatorFolderDraft is not { } draft) return _navigatorRows;
        var parentIndex = Array.FindIndex(_navigatorRows, row =>
            row.Key.Kind == OverviewNodeKind.Folder && row.Key.Id == draft.ParentFolderId);
        if (parentIndex < 0) return _navigatorRows;
        var parentDepth = _navigatorRows[parentIndex].Depth;
        var insertionIndex = parentIndex + 1;
        while (insertionIndex < _navigatorRows.Length &&
               _navigatorRows[insertionIndex].Depth > parentDepth)
        {
            if (_navigatorRows[insertionIndex].Depth == parentDepth + 1 &&
                _navigatorRows[insertionIndex].Key.Kind == OverviewNodeKind.Sheet)
                break;
            insertionIndex++;
        }

        var result = _navigatorRows.ToList();
        var parent = _navigatorRows[parentIndex];
        var draftContinuations = parent.Depth == 0
            ? Array.Empty<bool>()
            : [.. parent.AncestorContinuations ?? [], parent.HasNextSibling];
        var draftHasNextSibling = insertionIndex < _navigatorRows.Length &&
                                  _navigatorRows[insertionIndex].Depth == parentDepth + 1;
        result.Insert(insertionIndex, new CanvasNavigatorRow(
            new OverviewNodeKey(OverviewNodeKind.Folder, draft.Id),
            draft.Name,
            parentDepth + 1,
            draft.ParentFolderId,
            IsDraft: true,
            HasNextSibling: draftHasNextSibling,
            AncestorContinuations: draftContinuations));
        return result.ToArray();
    }

    private int NavigatorVisibleRowCount(ObserverSize viewport) => Math.Max(
        1,
        (int)Math.Floor((viewport.Height - NavigatorTop - NavigatorHeaderHeight - 8) /
                        NavigatorRowHeight));

    private void ClampNavigatorScroll()
    {
        var rows = NavigatorRowsForDisplay();
        ClampNavigatorScroll(rows.Length, NavigatorVisibleRowCount(ViewportSize()));
    }

    private void ClampNavigatorScroll(int rowCount, int visibleCount)
    {
        _navigatorScrollRow = Math.Clamp(_navigatorScrollRow, 0, Math.Max(0, rowCount - visibleCount));
    }

    private void EnsureNavigatorDraftVisible()
    {
        var rows = NavigatorRowsForDisplay();
        var index = Array.FindIndex(rows, row => row.IsDraft);
        if (index < 0) return;
        var visibleCount = NavigatorVisibleRowCount(ViewportSize());
        if (index < _navigatorScrollRow) _navigatorScrollRow = index;
        else if (index >= _navigatorScrollRow + visibleCount)
            _navigatorScrollRow = Math.Max(0, index - visibleCount + 1);
    }

    private bool TryNavigatorRowAt(PointF point, out CanvasNavigatorRow row)
    {
        row = default!;
        if (!_navigatorVisible || point.X < 0 || point.X > NavigatorWidth ||
            point.Y < NavigatorTop + NavigatorHeaderHeight)
            return false;
        var visibleIndex = (int)((point.Y - NavigatorTop - NavigatorHeaderHeight) /
                                 NavigatorRowHeight);
        if (visibleIndex < 0 || visibleIndex >= NavigatorVisibleRowCount(ViewportSize()))
            return false;
        var rows = NavigatorRowsForDisplay();
        var rowIndex = _navigatorScrollRow + visibleIndex;
        if (rowIndex < 0 || rowIndex >= rows.Length) return false;
        row = rows[rowIndex];
        return true;
    }

    private bool IsNavigatorDisclosureHit(PointF point, CanvasNavigatorRow row) =>
        row.CanExpand && point.X >= 8 + row.Depth * 16 && point.X <= 24 + row.Depth * 16;

    private void ToggleNavigatorRow(CanvasNavigatorRow row)
    {
        if (!row.CanExpand) return;
        if (row.Key.Kind == OverviewNodeKind.Folder)
        {
            if (!_collapsedNavigatorFolders.Add(row.Key.Id))
                _collapsedNavigatorFolders.Remove(row.Key.Id);
        }
        else if (row.Key.Kind == OverviewNodeKind.Sheet)
        {
            if (!_expandedNavigatorSheets.Add(row.Key.Id))
                _expandedNavigatorSheets.Remove(row.Key.Id);
        }

        _navigatorRows = BuildNavigatorRows(_snapshot);
        ClampNavigatorScroll();
        Invalidate();
    }

    private void ExpandNavigatorFolderPath(Guid folderId)
    {
        var folders = _snapshot.Folders.ToDictionary(folder => folder.Id);
        var visited = new HashSet<Guid>();
        var current = folderId;
        while (visited.Add(current) && folders.TryGetValue(current, out var folder))
        {
            _collapsedNavigatorFolders.Remove(current);
            if (folder.ParentId is not { } parentId) break;
            current = parentId;
        }
    }

    private NavigatorDropResolution ResolveNavigatorDrop(PointF point)
    {
        if (!_navigatorVisible || point.X < 0 || point.X > NavigatorWidth)
            return NavigatorDropResolution.Invalid("The pointer is outside the navigator.");
        var rows = NavigatorRowsForDisplay();
        var visibleCount = NavigatorVisibleRowCount(ViewportSize());
        var visibleRows = rows
            .Skip(_navigatorScrollRow)
            .Take(visibleCount)
            .Select((row, index) => new NavigatorDropRow(
                row.Key,
                row.ParentFolderId,
                NavigatorTop + NavigatorHeaderHeight + index * NavigatorRowHeight,
                NavigatorRowHeight))
            .Where(row => rows.All(candidate => candidate.Key != row.Key || !candidate.IsDraft))
            .ToArray();
        var kinds = _navigatorDragKeys.Select(key => key.Kind).Distinct().ToArray();
        return new NavigatorDropResolver().Resolve(
            visibleRows,
            point.Y,
            NavigatorTop + NavigatorHeaderHeight,
            Math.Max(NavigatorTop + NavigatorHeaderHeight, Size.Height - 8),
            kinds,
            _snapshot.RootFolderId);
    }

    private void AutoScrollNavigator(PointF point)
    {
        var now = DateTime.UtcNow;
        if (now - _navigatorLastAutoScrollUtc < TimeSpan.FromMilliseconds(70)) return;
        var rows = NavigatorRowsForDisplay();
        var visibleCount = NavigatorVisibleRowCount(ViewportSize());
        var previous = _navigatorScrollRow;
        if (point.Y <= NavigatorTop + NavigatorAutoScrollEdge)
            _navigatorScrollRow--;
        else if (point.Y >= Size.Height - NavigatorAutoScrollEdge)
            _navigatorScrollRow++;
        ClampNavigatorScroll(rows.Length, visibleCount);
        if (previous != _navigatorScrollRow)
        {
            _navigatorLastAutoScrollUtc = now;
            _navigatorHoverFolderId = null;
        }
    }

    private void UpdateNavigatorHoverExpansion()
    {
        var folderId = _navigatorDrop is { IsValid: true, HighlightFolderId: { } candidate }
            ? candidate
            : (Guid?)null;
        if (folderId is null || folderId == _snapshot.RootFolderId ||
            !_collapsedNavigatorFolders.Contains(folderId.Value))
        {
            _navigatorHoverFolderId = null;
            _navigatorHoverTimer.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        if (_navigatorHoverFolderId != folderId)
        {
            _navigatorHoverFolderId = folderId;
            _navigatorHoverStartedUtc = now;
            _navigatorHoverTimer.Stop();
            _navigatorHoverTimer.Start();
            return;
        }

        if (now - _navigatorHoverStartedUtc < NavigatorHoverExpandDelay) return;
        _collapsedNavigatorFolders.Remove(folderId.Value);
        _navigatorRows = BuildNavigatorRows(_snapshot);
        ClampNavigatorScroll();
        _navigatorHoverFolderId = null;
        _navigatorHoverTimer.Stop();
    }

    private CanvasNavigatorRow[] BuildNavigatorRows(ObserverSnapshot snapshot)
    {
        if (!snapshot.HasDocument) return [];
        var folders = snapshot.Folders.ToDictionary(folder => folder.Id);
        if (!folders.TryGetValue(snapshot.RootFolderId, out var root)) return [];
        var rows = new List<CanvasNavigatorRow>();
        var visited = new HashSet<Guid>();

        void AddFolder(
            ObserverFolderSnapshot folder,
            int depth,
            bool hasNextSibling,
            IReadOnlyList<bool> ancestorContinuations)
        {
            if (!visited.Add(folder.Id)) return;
            var childFolders = folders.Values.Where(candidate => candidate.ParentId == folder.Id)
                .OrderBy(candidate => candidate.Order)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var childSheets = snapshot.Sheets.Where(sheet => sheet.FolderId == folder.Id)
                .OrderBy(sheet => sheet.Order)
                .ThenBy(sheet => sheet.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var canExpand = childFolders.Length > 0 || childSheets.Length > 0;
            var expanded = !_collapsedNavigatorFolders.Contains(folder.Id);
            rows.Add(new CanvasNavigatorRow(
                new OverviewNodeKey(OverviewNodeKind.Folder, folder.Id),
                folder.Id == snapshot.RootFolderId
                    ? DocumentRootLabel(snapshot.DocumentName)
                    : folder.Name,
                depth,
                folder.ParentId ?? snapshot.RootFolderId,
                IsDocumentRoot: folder.Id == snapshot.RootFolderId,
                CanExpand: canExpand,
                IsExpanded: expanded,
                HasNextSibling: hasNextSibling,
                AncestorContinuations: ancestorContinuations));
            if (!expanded) return;
            var childCount = childFolders.Length + childSheets.Length;
            var childIndex = 0;
            var childContinuations = depth == 0
                ? Array.Empty<bool>()
                : [.. ancestorContinuations, hasNextSibling];
            foreach (var child in childFolders)
            {
                AddFolder(
                    child,
                    depth + 1,
                    childIndex < childCount - 1,
                    childContinuations);
                childIndex++;
            }
            foreach (var sheet in childSheets)
            {
                var sheetExpanded = _expandedNavigatorSheets.Contains(sheet.PageViewId);
                var sheetHasNextSibling = childIndex < childCount - 1;
                rows.Add(new CanvasNavigatorRow(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId),
                    sheet.Name,
                    depth + 1,
                    sheet.FolderId,
                    CanExpand: sheet.Details.Count > 0,
                    IsExpanded: sheetExpanded,
                    HasNextSibling: sheetHasNextSibling,
                    AncestorContinuations: childContinuations));
                childIndex++;
                if (!sheetExpanded) continue;
                bool[] detailContinuations = [.. childContinuations, sheetHasNextSibling];
                for (var detailIndex = 0; detailIndex < sheet.Details.Count; detailIndex++)
                {
                    var detail = sheet.Details[detailIndex];
                    rows.Add(new CanvasNavigatorRow(
                        new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId),
                        detail.Name,
                        depth + 2,
                        sheet.FolderId,
                        HasNextSibling: detailIndex < sheet.Details.Count - 1,
                        AncestorContinuations: detailContinuations));
                }
            }
        }

        AddFolder(root, 0, false, []);
        return rows.ToArray();
    }

    private static string DocumentRootLabel(string documentName)
    {
        var name = string.IsNullOrWhiteSpace(documentName)
            ? "Untitled Rhino document"
            : documentName;
        return name.EndsWith(".3dm", StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name}.3dm";
    }

    private void OnMouseDown(object? sender, MouseEventArgs eventArgs)
    {
        Focus();
        _pressScreen = Point(eventArgs.Location);
        _lastScreen = _pressScreen;
        _pressWorld = _camera.ScreenToWorld(_pressScreen, ViewportSize());
        _contextWorld = _pressWorld;
        _dragWorldDelta = new ObserverPoint();
        if (eventArgs.Buttons.HasFlag(MouseButtons.Alternate))
        {
            _dragMode = DragMode.ContextOrPan;
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Buttons.HasFlag(MouseButtons.Middle) ||
            (_spaceHeld && eventArgs.Buttons.HasFlag(MouseButtons.Primary)))
        {
            _dragMode = DragMode.Pan;
            eventArgs.Handled = true;
            return;
        }

        if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary)) return;
        if (TryNamedViewsModeToggleAt(eventArgs.Location, out var thumbnails))
        {
            _namedViewsThumbnailMode = thumbnails;
            _namedViewsScrollRow = 0;
            ClampNamedViewsScroll();
            Invalidate();
            if (_namedViewsThumbnailMode)
                NamedViewPreviewsRequested?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
            return;
        }

        if (TryNamedViewAt(eventArgs.Location, out var namedView))
        {
            _selectedNamedView = namedView;
            _dragNamedView = namedView;
            _dragMode = DragMode.NamedView;
            eventArgs.Handled = true;
            Invalidate();
            return;
        }

        if (IsNamedViewsActionAt(eventArgs.Location))
        {
            if (!string.IsNullOrWhiteSpace(_selectedNamedView))
                AssignNamedViewToSelectionRequested?.Invoke(
                    this,
                    new ObserverNamedViewSelectionRequestedEventArgs(_selectedNamedView));
            eventArgs.Handled = true;
            return;
        }

        if (TryNavigatorRowAt(eventArgs.Location, out var navigatorRow))
        {
            if (IsNavigatorDisclosureHit(eventArgs.Location, navigatorRow))
                ToggleNavigatorRow(navigatorRow);
            else if (!navigatorRow.IsDraft)
            {
                var movable = !navigatorRow.IsDocumentRoot &&
                              navigatorRow.Key.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet;
                var alreadySelected = _selection.Contains(navigatorRow.Key);
                _navigatorCollapseSelectionOnMouseUp = movable && alreadySelected &&
                                                         !IsAdditive(eventArgs.Modifiers);
                if (!_navigatorCollapseSelectionOnMouseUp)
                    SelectNavigatorKey(navigatorRow.Key, eventArgs.Modifiers);
                if (movable)
                {
                    _navigatorPressRow = navigatorRow;
                    var orderedVisible = NavigatorRowsForDisplay()
                        .Where(row => !row.IsDraft &&
                                      row.Key.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet &&
                                      _selection.Contains(row.Key))
                        .Select(row => row.Key)
                        .Distinct()
                        .ToList();
                    orderedVisible.AddRange(_selection
                        .Where(key => key.Kind is OverviewNodeKind.Folder or OverviewNodeKind.Sheet &&
                                      !orderedVisible.Contains(key))
                        .OrderBy(key => key.Kind)
                        .ThenBy(key => key.Id));
                    _navigatorDragKeys = orderedVisible.ToArray();
                    if (_navigatorDragKeys.Length == 0) _navigatorDragKeys = [navigatorRow.Key];
                    _dragMode = DragMode.NavigatorPending;
                }
            }
            eventArgs.Handled = true;
            Invalidate();
            return;
        }

        _hasCanvasPastePoint = true;

        var folderSummary = HitFolderSummaryAtScreen(eventArgs.Location);
        if (folderSummary is not null)
        {
            SelectFolder(folderSummary.FolderId, eventArgs.Modifiers);
            if (_packingMode == ObserverPackingMode.NestedFolders)
            {
                _dragFolderId = folderSummary.FolderId;
                _dragMode = DragMode.Folder;
            }
            else
            {
                _dragMode = DragMode.None;
            }
            eventArgs.Handled = true;
            return;
        }

        var card = _spatialIndex.HitSheet(_pressWorld);
        if (card is not null &&
            _presentation.TierForSheet(card.Sheet.PageViewId) != ObserverCanvasLodTier.Folder)
        {
            var tier = _presentation.TierForSheet(card.Sheet.PageViewId);
            var screenBounds = ScreenRect(card.Bounds, ViewportSize());
            var reorderHandle = tier == ObserverCanvasLodTier.Detail &&
                                eventArgs.Location.X <= screenBounds.Left + 24 &&
                                eventArgs.Location.Y <= screenBounds.Top + 24;
            var detail = tier == ObserverCanvasLodTier.Detail && !reorderHandle
                ? _spatialIndex.HitDetail(_pressWorld)
                : null;
            if (detail is not null && detail.SheetPageViewId == card.Sheet.PageViewId)
                SelectKey(new OverviewNodeKey(OverviewNodeKind.Detail, detail.Detail.DetailViewportId), eventArgs.Modifiers);
            else
                SelectSheet(card.Sheet.PageViewId, eventArgs.Modifiers);
            _reorderSheetId = card.Sheet.PageViewId;
            _dragMode = reorderHandle
                ? DragMode.Reorder
                : detail is not null
                    ? DragMode.Detail
                    : _packingMode == ObserverPackingMode.CompactSheets
                        ? DragMode.CompactSheet
                        : DragMode.Sheets;
        }
        else
        {
            var folder = _spatialIndex.HitFolderHeader(
                _pressWorld,
                ObserverPlacementPlanner.FolderHeaderHeight);
            if (folder is not null && FolderHasDrawableDescendant(folder.Folder.Id))
            {
                SelectFolder(folder.Folder.Id, eventArgs.Modifiers);
                _dragFolderId = folder.Folder.Id;
                _dragMode = DragMode.Folder;
            }
            else
            {
                if (!IsAdditive(eventArgs.Modifiers))
                    SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs([], null));
                _dragMode = DragMode.Lasso;
                _lassoWorld = new ObserverRect(_pressWorld.X, _pressWorld.Y, 0, 0);
            }
        }

        eventArgs.Handled = true;
    }

    private void OnMouseMove(object? sender, MouseEventArgs eventArgs)
    {
        var current = Point(eventArgs.Location);
        if (_dragMode == DragMode.None)
        {
            var overNavigator = _navigatorVisible &&
                                eventArgs.Location.X >= 0 && eventArgs.Location.X <= NavigatorWidth &&
                                eventArgs.Location.Y >= NavigatorTop;
            var overNamedViews = _namedViewsVisible &&
                                 eventArgs.Location.X >= Size.Width - NamedViewsWidth &&
                                 eventArgs.Location.Y >= NamedViewsTop;
            SetHoveredDetail(overNavigator || overNamedViews
                ? null
                : HitDetailAtScreen(eventArgs.Location)?.DetailViewportId);
            return;
        }
        if (_dragMode == DragMode.NamedView)
        {
            if (!string.IsNullOrWhiteSpace(_dragNamedView) &&
                eventArgs.Buttons.HasFlag(MouseButtons.Primary) &&
                Distance(_pressScreen, current) > 6)
            {
                var data = new DataObject();
                data.SetString(_dragNamedView, NamedViewDragType);
                DoDragDrop(data, DragEffects.Copy);
                ResetDrag();
            }

            eventArgs.Handled = true;
            return;
        }

        if (_dragMode == DragMode.NavigatorPending)
        {
            if (!eventArgs.Buttons.HasFlag(MouseButtons.Primary) ||
                Distance(_pressScreen, current) <= NavigatorDragActivationDistance)
            {
                eventArgs.Handled = true;
                return;
            }

            _dragMode = DragMode.Navigator;
            _navigatorCollapseSelectionOnMouseUp = false;
            _navigatorPointer = eventArgs.Location;
            _navigatorDragTimer.Start();
        }

        if (_dragMode == DragMode.Navigator)
        {
            _navigatorPointer = eventArgs.Location;
            AutoScrollNavigator(eventArgs.Location);
            _navigatorDrop = ResolveNavigatorDrop(eventArgs.Location);
            UpdateNavigatorHoverExpansion();
            Invalidate();
            eventArgs.Handled = true;
            return;
        }

        if (_dragMode == DragMode.ContextOrPan)
        {
            if (Distance(_pressScreen, current) <= RightPanActivationDistance)
            {
                eventArgs.Handled = true;
                return;
            }

            _dragMode = DragMode.Pan;
        }

        if (_dragMode == DragMode.Pan)
        {
            _camera = _camera.PanScreen(current.X - _lastScreen.X, current.Y - _lastScreen.Y);
            _lastScreen = current;
            NotifyViewChanged();
        }
        else
        {
            var world = _camera.ScreenToWorld(current, ViewportSize());
            _dragWorldDelta = world - _pressWorld;
            if (_dragMode == DragMode.Lasso)
            {
                _lassoWorld = new ObserverRect(
                    _pressWorld.X,
                    _pressWorld.Y,
                    world.X - _pressWorld.X,
                    world.Y - _pressWorld.Y);
            }

            Invalidate();
        }

        eventArgs.Handled = true;
    }

    private void OnMouseUp(object? sender, MouseEventArgs eventArgs)
    {
        var releaseScreen = Point(eventArgs.Location);
        var releaseWorld = _camera.ScreenToWorld(releaseScreen, ViewportSize());
        if (_dragMode == DragMode.NavigatorPending)
        {
            if (_navigatorCollapseSelectionOnMouseUp && _navigatorPressRow is { } pressed)
                SelectNavigatorKey(pressed.Key, Keys.None);
            ResetDrag();
            eventArgs.Handled = true;
            return;
        }

        if (_dragMode == DragMode.Navigator)
        {
            var resolution = ResolveNavigatorDrop(eventArgs.Location);
            if (resolution is { IsValid: true, Target: { } target })
            {
                var folderIds = _navigatorDragKeys
                    .Where(key => key.Kind == OverviewNodeKind.Folder)
                    .Select(key => key.Id)
                    .ToArray();
                var sheetIds = _navigatorDragKeys
                    .Where(key => key.Kind == OverviewNodeKind.Sheet)
                    .Select(key => key.Id)
                    .ToArray();
                HierarchyPlacementRequested?.Invoke(this,
                    new ObserverHierarchyPlacementRequestedEventArgs(folderIds, sheetIds, target));
            }

            ResetDrag();
            eventArgs.Handled = true;
            return;
        }

        if (_dragMode == DragMode.NamedView)
        {
            ResetDrag();
            eventArgs.Handled = true;
            return;
        }

        if (_dragMode == DragMode.ContextOrPan)
        {
            if (Distance(_pressScreen, releaseScreen) <= RightPanActivationDistance)
            {
                SelectAt(_contextWorld, eventArgs.Modifiers, preserveExistingIfHit: true);
                var contextPoint = new PointF((float)_pressScreen.X, (float)_pressScreen.Y);
                ResetDrag();
                ContextRequested?.Invoke(this, new ObserverContextRequestedEventArgs(
                    _contextWorld,
                    contextPoint,
                    HitFolderSummaryAtScreen(contextPoint)?.FolderId ??
                    HitFolderBody(_contextWorld)?.Folder.Id));
            }
            else
            {
                _camera = _camera.PanScreen(
                    releaseScreen.X - _lastScreen.X,
                    releaseScreen.Y - _lastScreen.Y);
                ResetDrag();
                NotifyViewChanged();
            }

            eventArgs.Handled = true;
            return;
        }

        if (_dragMode == DragMode.Sheets && Distance(_pressScreen, Point(eventArgs.Location)) > 4)
        {
            var selectedSheetIds = SelectedSheetIds();
            var destination = HitFolderBody(releaseWorld);
            if (destination is not null && selectedSheetIds.Any(sheetId =>
                    _layout.Sheets.TryGetValue(sheetId, out var card) &&
                    card.Sheet.FolderId != destination.Folder.Id))
            {
                HierarchyMoveRequested?.Invoke(this, new ObserverHierarchyMoveRequestedEventArgs(
                    destination.Folder.Id,
                    selectedSheetIds,
                    []));
            }
            else
            {
                var state = new ObserverPlacementPlanner().MoveSheets(
                    _snapshot,
                    _layout,
                    selectedSheetIds,
                    _dragWorldDelta);
                BoardStateRequested?.Invoke(this, new ObserverBoardStateRequestedEventArgs(
                    state,
                    $"Move {selectedSheetIds.Length} observer layout{(selectedSheetIds.Length == 1 ? string.Empty : "s")}"));
            }
        }
        else if (_dragMode == DragMode.Folder && _dragFolderId is { } folderId &&
                 Distance(_pressScreen, Point(eventArgs.Location)) > 4)
        {
            var state = new ObserverPlacementPlanner().MoveFolder(
                _snapshot,
                folderId,
                _dragWorldDelta);
            BoardStateRequested?.Invoke(this, new ObserverBoardStateRequestedEventArgs(
                state,
                "Move observer folder"));
        }
        else if (_dragMode == DragMode.Reorder && _reorderSheetId is { } movingId &&
                 Distance(_pressScreen, Point(eventArgs.Location)) > 4)
        {
            var target = _spatialIndex.HitSheet(releaseWorld);
            if (target is not null &&
                _presentation.TierForSheet(target.Sheet.PageViewId) != ObserverCanvasLodTier.Folder &&
                target.Sheet.PageViewId != movingId)
                ReorderRequested?.Invoke(this, new ObserverReorderRequestedEventArgs(
                    movingId,
                    target.Sheet.PageViewId));
        }
        else if (_dragMode == DragMode.Lasso && _lassoWorld is { } lasso)
        {
            var crossing = lasso.Width < 0;
            var keys = new List<OverviewNodeKey>();
            foreach (var card in _spatialIndex.QuerySheets(lasso)
                         .Where(card => crossing || lasso.Contains(card.Bounds)))
            {
                var tier = _presentation.TierForSheet(card.Sheet.PageViewId);
                if (tier == ObserverCanvasLodTier.Folder) continue;
                if (tier == ObserverCanvasLodTier.Detail)
                {
                    var details = card.Sheet.Details
                        .Select(detail => new
                        {
                            Detail = detail,
                            Bounds = ObserverSpatialIndex.DetailBounds(card.Bounds, detail.NormalizedBounds),
                        })
                        .Where(target => crossing
                            ? target.Bounds.Intersects(lasso)
                            : lasso.Contains(target.Bounds))
                        .Select(target => new OverviewNodeKey(
                            OverviewNodeKind.Detail,
                            target.Detail.DetailViewportId))
                        .ToArray();
                    if (details.Length > 0)
                    {
                        keys.AddRange(details);
                        continue;
                    }
                }

                keys.Add(new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId));
            }

            var lassoScreen = _camera.WorldToScreen(lasso, ViewportSize());
            keys.AddRange(_presentation.FolderSummaries
                .Where(summary => crossing
                    ? summary.ScreenBounds.Intersects(lassoScreen)
                    : lassoScreen.Contains(summary.ScreenBounds))
                .Select(summary => new OverviewNodeKey(OverviewNodeKind.Folder, summary.FolderId)));
            var distinctKeys = keys.Distinct().ToArray();
            SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(
                distinctKeys,
                distinctKeys.FirstOrDefault()));
        }

        ResetDrag();
        eventArgs.Handled = true;
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs eventArgs)
    {
        if (TryNamedViewAt(eventArgs.Location, out var namedView))
        {
            _selectedNamedView = namedView;
            AssignNamedViewToSelectionRequested?.Invoke(
                this,
                new ObserverNamedViewSelectionRequestedEventArgs(namedView));
            eventArgs.Handled = true;
            Invalidate();
            return;
        }

        if (TryNavigatorRowAt(eventArgs.Location, out var navigatorRow))
        {
            if (!navigatorRow.IsDraft)
            {
                SelectKey(navigatorRow.Key, eventArgs.Modifiers);
                Application.Instance.AsyncInvoke(FocusSelection);
            }

            eventArgs.Handled = true;
            return;
        }

        var folderSummary = HitFolderSummaryAtScreen(eventArgs.Location);
        if (folderSummary is not null)
        {
            SelectFolder(folderSummary.FolderId, eventArgs.Modifiers);
            ZoomFolderSummaryToSheets(folderSummary);
            eventArgs.Handled = true;
            return;
        }

        var world = _camera.ScreenToWorld(Point(eventArgs.Location), ViewportSize());
        var card = _spatialIndex.HitSheet(world);
        if (card is null) return;
        var tier = _presentation.TierForSheet(card.Sheet.PageViewId);
        if (tier == ObserverCanvasLodTier.Folder) return;
        if (tier == ObserverCanvasLodTier.Sheet)
        {
            SelectSheet(card.Sheet.PageViewId, eventArgs.Modifiers);
            ZoomSheetSummaryToDetails(card);
            eventArgs.Handled = true;
            return;
        }

        var detail = HitDetailAtWorld(world)?.Detail;
        var target = detail is null
            ? new OverviewNavigationTarget(card.Sheet.PageViewId)
            : new OverviewNavigationTarget(card.Sheet.PageViewId, detail.DetailViewportId);
        NavigationRequested?.Invoke(this, new ObserverNavigationRequestedEventArgs(target));
        eventArgs.Handled = true;
    }

    private void OnMouseWheel(object? sender, MouseEventArgs eventArgs)
    {
        var delta = eventArgs.Delta.Height;
        if (Math.Abs(delta) < float.Epsilon) return;
        if (_navigatorVisible && eventArgs.Location.X >= 0 && eventArgs.Location.X <= NavigatorWidth &&
            eventArgs.Location.Y >= NavigatorTop)
        {
            var rows = NavigatorRowsForDisplay();
            var visibleCount = NavigatorVisibleRowCount(ViewportSize());
            if (rows.Length > visibleCount)
            {
                _navigatorScrollRow += delta > 0 ? -1 : 1;
                ClampNavigatorScroll(rows.Length, visibleCount);
                Invalidate();
                eventArgs.Handled = true;
                return;
            }
        }

        if (_namedViewsVisible &&
            eventArgs.Location.X >= NamedViewsLeft(ViewportSize()) &&
            eventArgs.Location.Y >= NamedViewsTop)
        {
            var capacity = NamedViewsVisibleItemCount(ViewportSize());
            if (_snapshot.NamedViews.Count > capacity)
            {
                var step = _namedViewsThumbnailMode ? NamedViewsThumbnailColumns : 1;
                _namedViewsScrollRow += delta > 0 ? -step : step;
                ClampNamedViewsScroll(capacity);
                Invalidate();
                if (_namedViewsThumbnailMode)
                    NamedViewPreviewsRequested?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
                return;
            }
        }

        QueueCameraZoom(
            Math.Exp(delta * WheelZoomSensitivity),
            Point(eventArgs.Location));
        eventArgs.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_navigatorFolderDraft is { } draft)
        {
            if (eventArgs.Key == Keys.Enter)
            {
                var name = draft.Name.Trim();
                if (name.Length > 0)
                {
                    FolderDraftRequested?.Invoke(this, new ObserverFolderDraftRequestedEventArgs(
                        draft.Id,
                        draft.ParentFolderId,
                        name));
                }

                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Keys.Escape)
            {
                CancelNavigatorFolderDraft();
                eventArgs.Handled = true;
                return;
            }

            if (eventArgs.Key == Keys.Backspace)
            {
                var name = draft.SelectAll
                    ? string.Empty
                    : draft.Name.Length > 0
                        ? draft.Name[..^1]
                        : string.Empty;
                _navigatorFolderDraft = draft with { Name = name, SelectAll = false };
                Invalidate();
                eventArgs.Handled = true;
                return;
            }

            return;
        }

        if (HierarchyClipboard.IsCopyShortcut(eventArgs))
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
            return;
        }
        if (HierarchyClipboard.IsPasteShortcut(eventArgs))
        {
            var destination = _hasCanvasPastePoint ? HitFolderBody(_pressWorld)?.Folder.Id : null;
            PasteRequested?.Invoke(this, new ObserverPasteRequestedEventArgs(
                destination,
                _hasCanvasPastePoint
                    ? new ObserverPointRecord(_pressWorld.X, _pressWorld.Y)
                    : null));
            eventArgs.Handled = true;
            return;
        }

        if (eventArgs.Key == Keys.Space)
        {
            _spaceHeld = true;
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.Equal or Keys.Add)
        {
            Zoom(1.2);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.Minus or Keys.Subtract)
        {
            Zoom(1 / 1.2);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.F)
        {
            if (_selection.Count > 0) FocusSelection(); else FitAll();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.T)
        {
            TidyRequested?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.Delete or Keys.Backspace)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Escape)
        {
            if (ExitWorkspaceOnEscape)
            {
                ExitWorkspaceRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs([], null));
                ResetDrag();
            }
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Keys.Enter)
        {
            NavigateFirstSelection();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key is Keys.PageUp or Keys.PageDown)
        {
            ReorderStepRequested?.Invoke(this,
                new ObserverReorderStepRequestedEventArgs(eventArgs.Key == Keys.PageUp ? -1 : 1));
            eventArgs.Handled = true;
        }
        else if (eventArgs.Modifiers.HasFlag(Keys.Alt) &&
                 eventArgs.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            var delta = eventArgs.Key switch
            {
                Keys.Left => new ObserverPoint(-10, 0),
                Keys.Right => new ObserverPoint(10, 0),
                Keys.Up => new ObserverPoint(0, -10),
                _ => new ObserverPoint(0, 10),
            };
            NudgeSelection(delta);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Modifiers.HasFlag(Keys.Application) || eventArgs.Modifiers.HasFlag(Keys.Control))
        {
            if (eventArgs.Key == Keys.A)
            {
                var keys = _snapshot.Sheets
                    .Select(sheet => new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId))
                    .ToArray();
                SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(keys, keys.FirstOrDefault()));
                eventArgs.Handled = true;
            }
        }
        else if (eventArgs.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
        {
            var dx = eventArgs.Key == Keys.Left ? 40 : eventArgs.Key == Keys.Right ? -40 : 0;
            var dy = eventArgs.Key == Keys.Up ? 40 : eventArgs.Key == Keys.Down ? -40 : 0;
            _camera = _camera.PanScreen(dx, dy);
            NotifyViewChanged();
            eventArgs.Handled = true;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs eventArgs)
    {
        if (_navigatorFolderDraft is not { } draft || string.IsNullOrEmpty(eventArgs.Text)) return;
        var name = draft.SelectAll ? eventArgs.Text : draft.Name + eventArgs.Text;
        _navigatorFolderDraft = draft with { Name = name, SelectAll = false };
        EnsureNavigatorDraftVisible();
        Invalidate();
    }

    private void OnKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Keys.Space) _spaceHeld = false;
    }

    private void OnDragOver(object? sender, DragEventArgs eventArgs)
    {
        eventArgs.Effects = eventArgs.Data.Contains(NamedViewDragType) &&
                            HitDetailAtScreen(eventArgs.Location) is not null
            ? DragEffects.Copy
            : DragEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs eventArgs)
    {
        var name = eventArgs.Data.GetString(NamedViewDragType);
        var detail = HitDetailAtScreen(eventArgs.Location);
        if (!string.IsNullOrWhiteSpace(name) && detail is not null)
        {
            NamedViewRequested?.Invoke(this, new ObserverNamedViewRequestedEventArgs(
                name,
                [detail.DetailViewportId]));
            eventArgs.Effects = DragEffects.Copy;
        }
        else
        {
            eventArgs.Effects = DragEffects.None;
        }
    }

    private void SelectAt(ObserverPoint world, Keys modifiers, bool preserveExistingIfHit)
    {
        var screen = _camera.WorldToScreen(world, ViewportSize());
        var summary = HitFolderSummaryAtScreen(new PointF((float)screen.X, (float)screen.Y));
        if (summary is not null)
        {
            var summaryKey = new OverviewNodeKey(OverviewNodeKind.Folder, summary.FolderId);
            if (!preserveExistingIfHit || !_selection.Contains(summaryKey))
                SelectFolder(summary.FolderId, modifiers);
            return;
        }

        var sheet = _spatialIndex.HitSheet(world);
        if (sheet is not null &&
            _presentation.TierForSheet(sheet.Sheet.PageViewId) != ObserverCanvasLodTier.Folder)
        {
            var detail = HitDetailAtWorld(world)?.Detail;
            if (detail is not null)
            {
                var detailKey = new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId);
                if (!preserveExistingIfHit || !_selection.Contains(detailKey))
                    SelectKey(detailKey, modifiers);
                return;
            }

            if (!preserveExistingIfHit || !_selection.Contains(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.Sheet.PageViewId)))
                SelectSheet(sheet.Sheet.PageViewId, modifiers);
            return;
        }

        var folder = _spatialIndex.HitFolderHeader(world, ObserverPlacementPlanner.FolderHeaderHeight);
        if (folder is not null && FolderHasDrawableDescendant(folder.Folder.Id))
            SelectFolder(folder.Folder.Id, modifiers);
    }

    private void SelectSheet(Guid sheetId, Keys modifiers) =>
        SelectKey(new OverviewNodeKey(OverviewNodeKind.Sheet, sheetId), modifiers);

    private void SelectFolder(Guid folderId, Keys modifiers) =>
        SelectKey(new OverviewNodeKey(OverviewNodeKind.Folder, folderId), modifiers);

    private void SelectKey(OverviewNodeKey key, Keys modifiers)
    {
        var keys = IsAdditive(modifiers) ? _selection.ToHashSet() : [];
        if (IsAdditive(modifiers) && !keys.Add(key)) keys.Remove(key);
        else keys.Add(key);
        SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(keys.ToArray(), key));
    }

    private void SelectNavigatorKey(OverviewNodeKey key, Keys modifiers)
    {
        if (!modifiers.HasFlag(Keys.Shift))
        {
            SelectKey(key, modifiers);
            return;
        }

        var visibleKeys = NavigatorRowsForDisplay()
            .Where(row => !row.IsDraft)
            .Select(row => row.Key)
            .ToArray();
        var selection = new OverviewSelectionModel();
        selection.Replace(_selection, _selectionAnchor);
        selection.SelectRange(
            visibleKeys,
            key,
            additive: modifiers.HasFlag(Keys.Application) || modifiers.HasFlag(Keys.Control));
        SelectionRequested?.Invoke(
            this,
            new ObserverSelectionRequestedEventArgs(selection.Selected.ToArray(), selection.Anchor));
    }

    private static bool IsAdditive(Keys modifiers) =>
        modifiers.HasFlag(Keys.Application) || modifiers.HasFlag(Keys.Control) || modifiers.HasFlag(Keys.Shift);

    private ObserverFolderFrame? HitFolderBody(ObserverPoint world) =>
        _layout.Folders.Values
            .Where(frame => frame.Bounds.Contains(world))
            .OrderByDescending(frame => frame.Depth)
            .FirstOrDefault();

    private ObserverFolderSummary? HitFolderSummaryAtScreen(PointF screen)
    {
        var point = Point(screen);
        return _presentation.FolderSummaries
            .Where(summary => summary.ScreenBounds.Contains(point))
            .OrderBy(summary => summary.ScreenBounds.Width * summary.ScreenBounds.Height)
            .FirstOrDefault();
    }

    private ObserverDetailTarget? HitDetailAtWorld(ObserverPoint world)
    {
        var hit = _spatialIndex.HitDetail(world);
        return hit is not null &&
               _presentation.TierForSheet(hit.SheetPageViewId) == ObserverCanvasLodTier.Detail
            ? hit
            : null;
    }

    private ObserverDetailSnapshot? HitDetailAtScreen(PointF screen)
    {
        var world = _camera.ScreenToWorld(Point(screen), ViewportSize());
        return HitDetailAtWorld(world)?.Detail;
    }

    private void ZoomSheetSummaryToDetails(ObserverSheetCard card)
    {
        var shortEdge = Math.Max(1, Math.Min(card.Bounds.Width, card.Bounds.Height));
        var targetZoom = Math.Clamp(
            Math.Max(_camera.Zoom, (ObserverCanvasLodPolicy.EnterDetailPixels + 4) / shortEdge),
            ObserverCamera.MinimumZoom,
            ObserverCamera.MaximumZoom);
        _camera = new ObserverCamera(card.Bounds.Center, targetZoom);
        NotifyViewChanged();
    }

    private void ZoomFolderSummaryToSheets(ObserverFolderSummary summary)
    {
        var represented = summary.RepresentedSheetIds
            .Where(_layout.Sheets.ContainsKey)
            .Select(sheetId => _layout.Sheets[sheetId])
            .ToArray();
        if (represented.Length == 0)
        {
            _camera = ObserverCamera.Fit(summary.WorldBounds, ViewportSize(), 72);
            NotifyViewChanged();
            return;
        }

        var smallestShortEdge = represented.Min(card => Math.Min(card.Bounds.Width, card.Bounds.Height));
        var targetZoom = Math.Clamp(
            Math.Max(_camera.Zoom, (ObserverCanvasLodPolicy.EnterSheetPixels + 4) / Math.Max(1, smallestShortEdge)),
            ObserverCamera.MinimumZoom,
            ObserverCamera.MaximumZoom);
        _camera = new ObserverCamera(summary.WorldBounds.Center, targetZoom);
        NotifyViewChanged();
    }

    private void SetHoveredDetail(Guid? detailId)
    {
        if (_hoverDetailId == detailId) return;
        _hoverDetailId = detailId;
        Invalidate();
    }

    private void NavigateFirstSelection()
    {
        var first = _selection.FirstOrDefault();
        if (first == default) return;
        var target = NavigationTarget(first);
        if (target is { } navigation)
            NavigationRequested?.Invoke(this, new ObserverNavigationRequestedEventArgs(navigation));
    }

    private ObserverRect SelectedBounds()
    {
        var bounds = new ObserverRect();
        foreach (var key in _selection)
        {
            if (key.Kind == OverviewNodeKind.Sheet && _layout.Sheets.TryGetValue(key.Id, out var card))
                bounds = ObserverRect.Union(bounds, card.Bounds);
            else if (key.Kind == OverviewNodeKind.Folder && _layout.Folders.TryGetValue(key.Id, out var frame))
                bounds = ObserverRect.Union(bounds, frame.Bounds);
            else if (key.Kind == OverviewNodeKind.Detail)
            {
                var owner = _layout.Sheets.Values.FirstOrDefault(candidate =>
                    candidate.Sheet.Details.Any(detail => detail.DetailViewportId == key.Id));
                var detail = owner?.Sheet.Details.FirstOrDefault(candidate => candidate.DetailViewportId == key.Id);
                if (owner is not null && detail is not null)
                    bounds = ObserverRect.Union(bounds, DetailWorldRect(owner.Bounds, detail.NormalizedBounds));
            }
        }

        return bounds;
    }

    private void NudgeSelection(ObserverPoint delta)
    {
        if (_packingMode == ObserverPackingMode.CompactSheets) return;
        var state = _snapshot.CanvasState;
        var selectedFolders = _selection
            .Where(key => key.Kind == OverviewNodeKind.Folder)
            .Select(key => key.Id)
            .Where(folderId => !_selection.Any(key => key.Kind == OverviewNodeKind.Folder &&
                                                       key.Id != folderId &&
                                                       IsFolderDescendant(folderId, key.Id)))
            .ToArray();
        foreach (var folderId in selectedFolders)
            state = new ObserverPlacementPlanner().MoveFolder(_snapshot with { CanvasState = state }, folderId, delta);

        var selectedSheets = SelectedSheetIds()
            .Where(sheetId => _layout.Sheets.TryGetValue(sheetId, out var card) &&
                              !selectedFolders.Any(folderId => IsFolderDescendant(card.Sheet.FolderId, folderId)))
            .ToArray();
        if (selectedSheets.Length > 0)
            state = new ObserverPlacementPlanner().MoveSheets(
                _snapshot with { CanvasState = state }, _layout, selectedSheets, delta);
        if (!ObserverCanvasStateComparer.ContentEquals(state, _snapshot.CanvasState))
            BoardStateRequested?.Invoke(this, new ObserverBoardStateRequestedEventArgs(
                state,
                "Nudge observer selection"));
    }

    private Guid[] SelectedSheetIds()
        => _selection
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Select(key => key.Id)
            .Distinct()
            .ToArray();

    private ObserverRect PreviewBounds(ObserverRect bounds, Guid id)
    {
        if (_dragMode == DragMode.Sheets &&
            _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Sheet, id)))
            return bounds.Translate(_dragWorldDelta);
        if (_dragMode == DragMode.Folder && _dragFolderId is { } draggedFolderId)
        {
            if (_layout.Folders.ContainsKey(id) && IsFolderDescendant(id, draggedFolderId))
                return bounds.Translate(_dragWorldDelta);
            if (_layout.Sheets.TryGetValue(id, out var card) &&
                IsFolderDescendant(card.Sheet.FolderId, draggedFolderId))
                return bounds.Translate(_dragWorldDelta);
        }
        return bounds;
    }

    private bool IsFolderDescendant(Guid folderId, Guid ancestorId)
    {
        var folders = _snapshot.Folders.ToDictionary(folder => folder.Id);
        var visited = new HashSet<Guid>();
        Guid? current = folderId;
        while (current is { } id && visited.Add(id) && folders.TryGetValue(id, out var folder))
        {
            if (id == ancestorId) return true;
            current = folder.ParentId;
        }

        return false;
    }

    private bool ContainsKey(OverviewNodeKey key) => key.Kind switch
    {
        OverviewNodeKind.Folder => _snapshot.Folders.Any(folder => folder.Id == key.Id),
        OverviewNodeKind.Sheet => _snapshot.Sheets.Any(sheet => sheet.PageViewId == key.Id),
        OverviewNodeKind.Detail => _snapshot.Sheets.Any(sheet => sheet.Details.Any(detail => detail.DetailViewportId == key.Id)),
        _ => false,
    };

    private OverviewNavigationTarget? NavigationTarget(OverviewNodeKey key)
    {
        if (key.Kind == OverviewNodeKind.Sheet)
            return new OverviewNavigationTarget(key.Id);
        if (key.Kind == OverviewNodeKind.Detail)
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == key.Id));
            return owner is null ? null : new OverviewNavigationTarget(owner.PageViewId, key.Id);
        }

        return null;
    }

    private void NotifyViewChanged()
    {
        StopQueuedCameraInput();
        RefreshPresentation();
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void QueueCameraPan(double screenX, double screenY)
    {
        if (Math.Abs(screenX) < double.Epsilon && Math.Abs(screenY) < double.Epsilon) return;
        _pendingPanScreen += new ObserverPoint(screenX, screenY);
        ScheduleCameraInputFrame();
    }

    private void QueueCameraZoom(double factor, ObserverPoint anchorScreen)
    {
        if (!double.IsFinite(factor) || factor <= 0 || Math.Abs(factor - 1) < double.Epsilon) return;
        _pendingZoomFactor *= factor;
        _pendingZoomAnchorScreen = anchorScreen;
        ScheduleCameraInputFrame();
    }

    private void ScheduleCameraInputFrame()
    {
        if (_cameraInputFrameScheduled) return;
        _cameraInputFrameScheduled = true;
        _cameraInputFrameTimer.Start();
    }

    private void FlushQueuedCameraInput()
    {
        _cameraInputFrameTimer.Stop();
        _cameraInputFrameScheduled = false;
        var pan = _pendingPanScreen;
        var zoomFactor = _pendingZoomFactor;
        var zoomAnchor = _pendingZoomAnchorScreen;
        _pendingPanScreen = new ObserverPoint();
        _pendingZoomFactor = 1;

        if (Math.Abs(pan.X) < double.Epsilon &&
            Math.Abs(pan.Y) < double.Epsilon &&
            Math.Abs(zoomFactor - 1) < double.Epsilon)
            return;

        if (Math.Abs(pan.X) >= double.Epsilon || Math.Abs(pan.Y) >= double.Epsilon)
            _camera = _camera.PanScreen(pan.X, pan.Y);
        if (Math.Abs(zoomFactor - 1) >= double.Epsilon)
            _camera = _camera.ZoomAt(zoomAnchor, zoomFactor, ViewportSize());

        RefreshPresentation();
        _cameraInputSettleTimer.Stop();
        _cameraInputSettleTimer.Start();
    }

    private void StopQueuedCameraInput()
    {
        _cameraInputFrameTimer.Stop();
        _cameraInputSettleTimer.Stop();
        _cameraInputFrameScheduled = false;
        _pendingPanScreen = new ObserverPoint();
        _pendingZoomFactor = 1;
    }

    private bool IsCanvasOverlay(PointF point)
    {
        if (_navigatorVisible && point.X >= 0 && point.X <= NavigatorWidth && point.Y >= NavigatorTop)
            return true;
        return _namedViewsVisible &&
               point.X >= NamedViewsLeft(ViewportSize()) &&
               point.Y >= NamedViewsTop;
    }

    partial void AttachNativeTrackpadInput();

    partial void DetachNativeTrackpadInput();

    private void RefreshPresentation()
    {
        _presentation = _lodPolicy.Evaluate(
            _snapshot,
            _layout,
            _camera,
            ViewportSize(),
            _packingMode,
            _presentation);
        _drawableFolderIds = BuildDrawableFolderIds();
        if (_hoverDetailId is { } detailId)
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == detailId));
            if (owner is null || _presentation.TierForSheet(owner.PageViewId) != ObserverCanvasLodTier.Detail)
                _hoverDetailId = null;
        }
        Invalidate();
    }

    private HashSet<Guid> BuildDrawableFolderIds()
    {
        var folders = _snapshot.Folders.ToDictionary(folder => folder.Id);
        var result = new HashSet<Guid>();
        foreach (var card in _layout.Sheets.Values)
        {
            if (_presentation.TierForSheet(card.Sheet.PageViewId) == ObserverCanvasLodTier.Folder)
                continue;

            Guid? current = card.Sheet.FolderId;
            while (current is { } folderId &&
                   folders.TryGetValue(folderId, out var folder))
            {
                if (!result.Add(folderId)) break;
                current = folder.ParentId;
            }
        }

        return result;
    }

    private void ResetDrag()
    {
        _dragMode = DragMode.None;
        _dragNamedView = null;
        _dragWorldDelta = new ObserverPoint();
        _dragFolderId = null;
        _reorderSheetId = null;
        _lassoWorld = null;
        _navigatorPressRow = null;
        _navigatorDragKeys = [];
        _navigatorCollapseSelectionOnMouseUp = false;
        _navigatorDrop = null;
        _navigatorHoverFolderId = null;
        _navigatorDragTimer.Stop();
        _navigatorHoverTimer.Stop();
        Invalidate();
    }

    private ObserverSize ViewportSize() => new(Math.Max(1, Size.Width), Math.Max(1, Size.Height));

    private RectangleF ScreenRect(ObserverRect world, ObserverSize viewport)
    {
        var screen = _camera.WorldToScreen(world, viewport);
        return new RectangleF((float)screen.X, (float)screen.Y, (float)screen.Width, (float)screen.Height);
    }

    private static RectangleF DetailScreenRect(ObserverRect normalized, RectangleF card) => new(
        card.Left + (float)(normalized.Left * card.Width),
        card.Top + (float)(normalized.Top * card.Height),
        (float)(normalized.Width * card.Width),
        (float)(normalized.Height * card.Height));

    private static ObserverRect DetailWorldRect(ObserverRect card, ObserverRect normalized) =>
        ObserverSpatialIndex.DetailBounds(card, normalized);

    private static ObserverPoint Point(PointF point) => new(point.X, point.Y);
    private static double Distance(ObserverPoint first, ObserverPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private sealed record PreviewEntry(OverviewThumbnailKey Key, Bitmap Bitmap);

    private sealed record NamedViewPreviewEntry(long ContentVersion, Bitmap Bitmap);

    private sealed record CanvasNavigatorRow(
        OverviewNodeKey Key,
        string Label,
        int Depth,
        Guid ParentFolderId,
        bool IsDraft = false,
        bool IsDocumentRoot = false,
        bool CanExpand = false,
        bool IsExpanded = false,
        bool HasNextSibling = false,
        IReadOnlyList<bool>? AncestorContinuations = null);

    private sealed record NavigatorFolderDraft(
        Guid Id,
        Guid ParentFolderId,
        string Name,
        bool SelectAll);

    private enum DragMode
    {
        None,
        NavigatorPending,
        Navigator,
        ContextOrPan,
        Pan,
        Sheets,
        CompactSheet,
        Folder,
        Reorder,
        Detail,
        Lasso,
        NamedView,
    }
}

internal sealed class ObserverHierarchyPlacementRequestedEventArgs : EventArgs
{
    internal ObserverHierarchyPlacementRequestedEventArgs(
        IReadOnlyList<Guid> folderIds,
        IReadOnlyList<Guid> sheetIds,
        HierarchyPlacementTarget target)
    {
        FolderIds = folderIds;
        SheetIds = sheetIds;
        Target = target;
    }

    internal IReadOnlyList<Guid> FolderIds { get; }
    internal IReadOnlyList<Guid> SheetIds { get; }
    internal HierarchyPlacementTarget Target { get; }
}

internal sealed class ObserverNamedViewSelectionRequestedEventArgs : EventArgs
{
    internal ObserverNamedViewSelectionRequestedEventArgs(string namedViewName)
    {
        NamedViewName = namedViewName;
    }

    internal string NamedViewName { get; }
}

internal sealed class ObserverFolderDraftRequestedEventArgs : EventArgs
{
    internal ObserverFolderDraftRequestedEventArgs(Guid folderId, Guid parentFolderId, string name)
    {
        FolderId = folderId;
        ParentFolderId = parentFolderId;
        Name = name;
    }

    internal Guid FolderId { get; }
    internal Guid ParentFolderId { get; }
    internal string Name { get; }
}

internal sealed class ObserverBoardStateRequestedEventArgs : EventArgs
{
    internal ObserverBoardStateRequestedEventArgs(ObserverCanvasState state, string undoDescription)
    {
        State = state;
        UndoDescription = undoDescription;
    }

    internal ObserverCanvasState State { get; }
    internal string UndoDescription { get; }
}

internal sealed class ObserverSelectionRequestedEventArgs : EventArgs
{
    internal ObserverSelectionRequestedEventArgs(
        IReadOnlyList<OverviewNodeKey> selection,
        OverviewNodeKey? anchor)
    {
        Selection = selection;
        Anchor = anchor;
    }

    internal IReadOnlyList<OverviewNodeKey> Selection { get; }
    internal OverviewNodeKey? Anchor { get; }
}

internal sealed class ObserverNavigationRequestedEventArgs : EventArgs
{
    internal ObserverNavigationRequestedEventArgs(OverviewNavigationTarget target) => Target = target;
    internal OverviewNavigationTarget Target { get; }
}

internal sealed class ObserverHierarchyMoveRequestedEventArgs : EventArgs
{
    internal ObserverHierarchyMoveRequestedEventArgs(
        Guid destinationFolderId,
        IReadOnlyList<Guid> sheetIds,
        IReadOnlyList<Guid> folderIds)
    {
        DestinationFolderId = destinationFolderId;
        SheetIds = sheetIds;
        FolderIds = folderIds;
    }

    internal Guid DestinationFolderId { get; }
    internal IReadOnlyList<Guid> SheetIds { get; }
    internal IReadOnlyList<Guid> FolderIds { get; }
}

internal sealed class ObserverReorderRequestedEventArgs : EventArgs
{
    internal ObserverReorderRequestedEventArgs(Guid movingSheetId, Guid beforeSheetId)
    {
        MovingSheetId = movingSheetId;
        BeforeSheetId = beforeSheetId;
    }

    internal Guid MovingSheetId { get; }
    internal Guid BeforeSheetId { get; }
}

internal sealed class ObserverReorderStepRequestedEventArgs : EventArgs
{
    internal ObserverReorderStepRequestedEventArgs(int direction) => Direction = Math.Sign(direction);
    internal int Direction { get; }
}

internal sealed class ObserverNamedViewRequestedEventArgs : EventArgs
{
    internal ObserverNamedViewRequestedEventArgs(string namedViewName, IReadOnlyList<Guid> detailViewportIds)
    {
        NamedViewName = namedViewName;
        DetailViewportIds = detailViewportIds;
    }

    internal string NamedViewName { get; }
    internal IReadOnlyList<Guid> DetailViewportIds { get; }
}

internal sealed class ObserverContextRequestedEventArgs : EventArgs
{
    internal ObserverContextRequestedEventArgs(
        ObserverPoint worldPoint,
        PointF controlPoint,
        Guid? destinationFolderId)
    {
        WorldPoint = worldPoint;
        ControlPoint = controlPoint;
        DestinationFolderId = destinationFolderId;
    }

    internal ObserverPoint WorldPoint { get; }
    internal PointF ControlPoint { get; }
    internal Guid? DestinationFolderId { get; }
}

internal sealed class ObserverPasteRequestedEventArgs(
    Guid? destinationFolderId,
    ObserverPointRecord? targetOrigin) : EventArgs
{
    internal Guid? DestinationFolderId { get; } = destinationFolderId;
    internal ObserverPointRecord? TargetOrigin { get; } = targetOrigin;
}
