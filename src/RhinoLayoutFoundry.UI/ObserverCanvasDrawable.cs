using Eto.Drawing;
using Eto.Forms;
using RhinoLayoutFoundry.Core.Domain;
using RhinoLayoutFoundry.Core.Observer;
using RhinoLayoutFoundry.Core.Overview;

namespace RhinoLayoutFoundry.UI;

internal sealed class ObserverCanvasDrawable : Drawable
{
    internal const string NamedViewDragType = "application/x-layout-foundry-named-view";
    private const double RightPanActivationDistance = 5;
    private const int NavigatorWidth = 260;
    private const int NavigatorTop = 38;
    private const int NavigatorHeaderHeight = 28;
    private const int NavigatorRowHeight = 24;
    private const int NamedViewsWidth = 224;
    private const int NamedViewsTop = 38;
    private const int NamedViewsHeaderHeight = 28;
    private const int NamedViewsRowHeight = 24;
    private const int NamedViewsActionHeight = 30;
    private const int NamedViewsThumbnailColumns = 2;
    private const int NamedViewsThumbnailCardHeight = 92;
    private readonly Font _folderFont = SystemFonts.Bold(11);
    private readonly Font _sheetFont = SystemFonts.Bold(10);
    private readonly Font _smallFont = SystemFonts.Default(8);
    private readonly Dictionary<Guid, PreviewEntry> _previews = [];
    private readonly Dictionary<string, NamedViewPreviewEntry> _namedViewPreviews =
        new(StringComparer.OrdinalIgnoreCase);
    private ObserverSnapshot _snapshot = ObserverSnapshot.NoDocument;
    private OverviewFilterProjection _filter = new(false, new HashSet<OverviewNodeKey>(), new HashSet<Guid>());
    private ObserverBoardLayout _layout = ObserverBoardLayout.Empty;
    private ObserverSpatialIndex _spatialIndex = new(ObserverBoardLayout.Empty);
    private ObserverCamera _camera = ObserverCamera.Default;
    private HashSet<OverviewNodeKey> _selection = [];
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
    private NavigatorFolderDraft? _navigatorFolderDraft;
    private readonly HashSet<Guid> _collapsedNavigatorFolders = [];
    private readonly HashSet<Guid> _expandedNavigatorSheets = [];
    private int _namedViewsScrollRow;
    private string? _selectedNamedView;
    private string? _dragNamedView;

    internal ObserverCanvasDrawable()
        : base(true)
    {
        BackgroundColor = FoundryTheme.CanvasBackground;
        CanFocus = true;
        AllowDrop = true;
        Paint += OnPaint;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseDoubleClick += OnMouseDoubleClick;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        TextInput += OnTextInput;
        DragOver += OnDragOver;
        DragDrop += OnDragDrop;
    }

    internal event EventHandler? ViewChanged;
    internal event EventHandler<ObserverBoardStateRequestedEventArgs>? BoardStateRequested;
    internal event EventHandler<ObserverSelectionRequestedEventArgs>? SelectionRequested;
    internal event EventHandler<ObserverNavigationRequestedEventArgs>? NavigationRequested;
    internal event EventHandler<ObserverHierarchyMoveRequestedEventArgs>? HierarchyMoveRequested;
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

    internal ObserverCamera Camera => _camera;
    internal ObserverBoardLayout BoardLayout => _layout;
    internal ObserverSnapshot Snapshot => _snapshot;
    internal bool ExitWorkspaceOnEscape { get; set; }

    internal void SetSnapshot(ObserverSnapshot snapshot, bool fit)
    {
        _snapshot = snapshot ?? ObserverSnapshot.NoDocument;
        _layout = new ObserverPlacementPlanner().Arrange(_snapshot);
        _spatialIndex = new ObserverSpatialIndex(_layout);
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

    internal void SetSelection(IEnumerable<OverviewNodeKey> selection)
    {
        _selection = selection.Where(ContainsKey).ToHashSet();
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
        return _spatialIndex.QuerySheets(visible.Inflate(overscan));
    }

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
            DrawFolder(graphics, frame, viewport);
        }

        foreach (var card in _spatialIndex.QuerySheets(visibleWorld))
        {
            DrawSheet(graphics, card, viewport);
        }

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
        if (_camera.Zoom < 0.18) return;
        var spacing = 40d;
        var visible = _camera.VisibleWorld(viewport);
        var startX = Math.Floor(visible.Left / spacing) * spacing;
        var startY = Math.Floor(visible.Top / spacing) * spacing;
        var color = FoundryTheme.CanvasGrid;
        for (var x = startX; x <= visible.Right; x += spacing)
        for (var y = startY; y <= visible.Bottom; y += spacing)
        {
            var point = _camera.WorldToScreen(new ObserverPoint(x, y), viewport);
            graphics.FillRectangle(color, (float)point.X, (float)point.Y, 1, 1);
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
        graphics.FillRectangle(FoundryTheme.WithAlpha(
                FoundryTheme.CanvasSubtleSurface,
                emphasized ? 220 : 55),
            bounds.X, bounds.Y, bounds.Width, (float)Math.Min(bounds.Height, headerHeight));
        graphics.DrawText(
            _folderFont,
            emphasized ? FoundryTheme.PrimaryText : FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 64),
            bounds.X + 10,
            bounds.Y + 6,
            $"▾  {frame.Folder.Name}   ·   {frame.DirectSheetCount} layout{(frame.DirectSheetCount == 1 ? string.Empty : "s")}");
    }

    private void DrawSheet(Graphics graphics, ObserverSheetCard card, ObserverSize viewport)
    {
        var worldBounds = PreviewBounds(card.Bounds, card.Sheet.PageViewId);
        var bounds = ScreenRect(worldBounds, viewport);
        if (bounds.Width < 3 || bounds.Height < 3) return;
        var key = new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId);
        var selected = _selection.Contains(key);
        var emphasized = _filter.Emphasizes(key);
        var hasSelectedDetail = card.Sheet.Details.Any(detail =>
            _selection.Contains(new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId)));
        graphics.FillRectangle(Color.FromArgb(0, 0, 0, emphasized ? 45 : 12),
            bounds.X + 4, bounds.Y + 5, bounds.Width, bounds.Height);
        graphics.FillRectangle(Colors.White, bounds);
        if (_previews.TryGetValue(card.Sheet.PageViewId, out var preview) &&
            preview.Key.ContentVersion == card.Sheet.PreviewContentVersion)
        {
            graphics.DrawImage(preview.Bitmap, bounds);
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

        graphics.DrawText(
            _sheetFont,
            emphasized ? FoundryTheme.PrimaryText : FoundryTheme.WithAlpha(FoundryTheme.PrimaryText, 64),
            bounds.Left,
            bounds.Bottom + 5,
            card.Sheet.Name);
        if (selected && _dragMode == DragMode.Sheets && _dragWorldDelta != new ObserverPoint())
        {
            graphics.DrawRectangle(
                new Pen(FoundryTheme.WithAlpha(FoundryTheme.SelectionAccent, 180), 1),
                bounds);
        }
    }

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
            if (!sheetSelected && !detailSelected) continue;
            var rect = DetailScreenRect(detail.NormalizedBounds, bounds);
            graphics.DrawRectangle(
                new Pen(
                    FoundryTheme.WithAlpha(
                        FoundryTheme.SelectionAccent,
                        detailSelected ? 255 : 180),
                    detailSelected ? 3 : 1),
                rect);
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

        DrawOverlayText(
            graphics,
            _folderFont,
            FoundryTheme.PrimaryText,
            8,
            NavigatorTop + 5,
            "Navigator");
        for (var visibleIndex = 0; visibleIndex < visibleCount; visibleIndex++)
        {
            var rowIndex = _navigatorScrollRow + visibleIndex;
            if (rowIndex >= rows.Length) break;
            var row = rows[rowIndex];
            var y = NavigatorTop + NavigatorHeaderHeight + visibleIndex * NavigatorRowHeight;
            var selected = _selection.Contains(row.Key);
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
            DrawOverlayText(
                graphics,
                _sheetFont,
                emphasized ? FoundryTheme.PrimaryText : FoundryTheme.WithAlpha(FoundryTheme.MutedText, 80),
                disclosureX + 16,
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
        var active = _namedViewsThumbnailMode ? thumbnails : list;
        graphics.FillRectangle(FoundryTheme.SelectionAccent, active.Left + 3, active.Bottom - 1,
            active.Width - 6, 2);
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
        result.Insert(insertionIndex, new CanvasNavigatorRow(
            new OverviewNodeKey(OverviewNodeKind.Folder, draft.Id),
            $"📁  {draft.Name}",
            parentDepth + 1,
            IsDraft: true));
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

    private CanvasNavigatorRow[] BuildNavigatorRows(ObserverSnapshot snapshot)
    {
        if (!snapshot.HasDocument) return [];
        var folders = snapshot.Folders.ToDictionary(folder => folder.Id);
        if (!folders.TryGetValue(snapshot.RootFolderId, out var root)) return [];
        var rows = new List<CanvasNavigatorRow>();
        var visited = new HashSet<Guid>();

        void AddFolder(ObserverFolderSnapshot folder, int depth)
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
                $"📁  {folder.Name}",
                depth,
                CanExpand: canExpand,
                IsExpanded: expanded));
            if (!expanded) return;
            foreach (var child in childFolders)
                AddFolder(child, depth + 1);
            foreach (var sheet in childSheets)
            {
                var sheetExpanded = _expandedNavigatorSheets.Contains(sheet.PageViewId);
                rows.Add(new CanvasNavigatorRow(
                    new OverviewNodeKey(OverviewNodeKind.Sheet, sheet.PageViewId),
                    $"▣  {sheet.Name}",
                    depth + 1,
                    CanExpand: sheet.Details.Count > 0,
                    IsExpanded: sheetExpanded));
                if (!sheetExpanded) continue;
                foreach (var detail in sheet.Details)
                    rows.Add(new CanvasNavigatorRow(
                        new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId),
                        $"⌗  {detail.Name}",
                        depth + 2));
            }
        }

        AddFolder(root, 0);
        return rows.ToArray();
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
                SelectKey(navigatorRow.Key, eventArgs.Modifiers);
            eventArgs.Handled = true;
            Invalidate();
            return;
        }

        var card = _spatialIndex.HitSheet(_pressWorld);
        if (card is not null)
        {
            var screenBounds = ScreenRect(card.Bounds, ViewportSize());
            var reorderHandle = eventArgs.Location.X <= screenBounds.Left + 24 &&
                                eventArgs.Location.Y <= screenBounds.Top + 24;
            var detail = reorderHandle ? null : HitDetail(card, _pressWorld);
            if (detail is not null)
                SelectKey(new OverviewNodeKey(OverviewNodeKind.Detail, detail.DetailViewportId), eventArgs.Modifiers);
            else
                SelectSheet(card.Sheet.PageViewId, eventArgs.Modifiers);
            _reorderSheetId = card.Sheet.PageViewId;
            _dragMode = reorderHandle
                ? DragMode.Reorder
                : DragMode.Sheets;
        }
        else
        {
            var folder = _spatialIndex.HitFolderHeader(
                _pressWorld,
                ObserverPlacementPlanner.FolderHeaderHeight);
            if (folder is not null)
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
        if (_dragMode == DragMode.None) return;
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
                    contextPoint));
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
            if (target is not null && target.Sheet.PageViewId != movingId)
                ReorderRequested?.Invoke(this, new ObserverReorderRequestedEventArgs(
                    movingId,
                    target.Sheet.PageViewId));
        }
        else if (_dragMode == DragMode.Lasso && _lassoWorld is { } lasso)
        {
            var crossing = lasso.Width < 0;
            var keys = _spatialIndex.QuerySheets(lasso)
                .Where(card => crossing || lasso.Contains(card.Bounds))
                .Select(card => new OverviewNodeKey(OverviewNodeKind.Sheet, card.Sheet.PageViewId))
                .ToArray();
            SelectionRequested?.Invoke(this, new ObserverSelectionRequestedEventArgs(
                keys,
                keys.FirstOrDefault()));
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

        var world = _camera.ScreenToWorld(Point(eventArgs.Location), ViewportSize());
        var card = _spatialIndex.HitSheet(world);
        if (card is null) return;
        var detail = HitDetail(card, world);
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

        _camera = _camera.ZoomAt(
            Point(eventArgs.Location),
            delta > 0 ? 1.12 : 1 / 1.12,
            ViewportSize());
        NotifyViewChanged();
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
        var sheet = _spatialIndex.HitSheet(world);
        if (sheet is not null)
        {
            var detail = HitDetail(sheet, world);
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
        if (folder is not null) SelectFolder(folder.Folder.Id, modifiers);
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

    private static bool IsAdditive(Keys modifiers) =>
        modifiers.HasFlag(Keys.Application) || modifiers.HasFlag(Keys.Control) || modifiers.HasFlag(Keys.Shift);

    private ObserverFolderFrame? HitFolderBody(ObserverPoint world) =>
        _layout.Folders.Values
            .Where(frame => frame.Bounds.Contains(world))
            .OrderByDescending(frame => frame.Depth)
            .FirstOrDefault();

    private ObserverDetailSnapshot? HitDetailAtScreen(PointF screen)
    {
        var world = _camera.ScreenToWorld(Point(screen), ViewportSize());
        var card = _spatialIndex.HitSheet(world);
        return card is null ? null : HitDetail(card, world);
    }

    private static ObserverDetailSnapshot? HitDetail(ObserverSheetCard card, ObserverPoint world)
    {
        var localX = (world.X - card.Bounds.Left) / card.Bounds.Width;
        var localY = (world.Y - card.Bounds.Top) / card.Bounds.Height;
        var point = new ObserverPoint(localX, localY);
        return card.Sheet.Details
            .Where(detail => detail.NormalizedBounds.Contains(point))
            .OrderBy(detail => detail.NormalizedBounds.Width * detail.NormalizedBounds.Height)
            .FirstOrDefault();
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
    {
        var result = _selection
            .Where(key => key.Kind == OverviewNodeKind.Sheet)
            .Select(key => key.Id)
            .ToHashSet();
        foreach (var detailId in _selection
                     .Where(key => key.Kind == OverviewNodeKind.Detail)
                     .Select(key => key.Id))
        {
            var owner = _snapshot.Sheets.FirstOrDefault(sheet =>
                sheet.Details.Any(detail => detail.DetailViewportId == detailId));
            if (owner is not null) result.Add(owner.PageViewId);
        }

        return result.ToArray();
    }

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
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetDrag()
    {
        _dragMode = DragMode.None;
        _dragNamedView = null;
        _dragWorldDelta = new ObserverPoint();
        _dragFolderId = null;
        _reorderSheetId = null;
        _lassoWorld = null;
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

    private static ObserverRect DetailWorldRect(ObserverRect card, ObserverRect normalized) => new(
        card.Left + normalized.Left * card.Width,
        card.Top + normalized.Top * card.Height,
        normalized.Width * card.Width,
        normalized.Height * card.Height);

    private static ObserverPoint Point(PointF point) => new(point.X, point.Y);
    private static double Distance(ObserverPoint first, ObserverPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private sealed record PreviewEntry(OverviewThumbnailKey Key, Bitmap Bitmap);

    private sealed record NamedViewPreviewEntry(long ContentVersion, Bitmap Bitmap);

    private sealed record CanvasNavigatorRow(
        OverviewNodeKey Key,
        string Label,
        int Depth,
        bool IsDraft = false,
        bool CanExpand = false,
        bool IsExpanded = false);

    private sealed record NavigatorFolderDraft(
        Guid Id,
        Guid ParentFolderId,
        string Name,
        bool SelectAll);

    private enum DragMode
    {
        None,
        ContextOrPan,
        Pan,
        Sheets,
        Folder,
        Reorder,
        Lasso,
        NamedView,
    }
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
    internal ObserverContextRequestedEventArgs(ObserverPoint worldPoint, PointF controlPoint)
    {
        WorldPoint = worldPoint;
        ControlPoint = controlPoint;
    }

    internal ObserverPoint WorldPoint { get; }
    internal PointF ControlPoint { get; }
}
